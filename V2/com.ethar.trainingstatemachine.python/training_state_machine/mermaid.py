# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""The training builder's mermaid dialect: parse a markdown document containing
a mermaid flowchart into a training scenario, and generate such a document back
from a scenario. A 1:1 behavioural twin of the C# ``TrainingMermaidBuilder``.

Dialect (see Documentation/Training-Builder.md for the full spec):

- Scenario name  = the document's first ``# heading``.
- One node per step, in first-appearance order. Node text segments split on
  ``|``: title, description, then sigil segments — ``@class: world label``
  (detectedClass + label), ``#modelRef``, ``!imageRef``, ``?waitingClass``
  (explicit override, only needed for nodes no edge reaches).
- Edges carry flow: a plain label is the action button text (comma-separated
  for several options); an ``@class`` token in the label is the step's result
  class. Without an ``@`` token, an action edge mints the target node's id
  (lowercased) as the synthetic result class. An edge to the ``END``
  pseudo-node ends the scenario (empty result, unless the label carries
  ``@class`` — a deliberately terminal class).
- ``%% minimumDetectionConfidence: 0.5`` inside the block sets the config gate.
- Branching (edges from one node to different targets) is NOT expressible in
  the current linear model and is reported as an error.
"""

import re
from typing import Dict, List, Optional, Tuple

from .scenario import TrainingScenario, TrainingStep
from .validation import ValidationReport, validate_scenario

END_NODE_ID = "END"

_NODE_TOKEN = re.compile(
    r'^([A-Za-z_][\w-]*)\s*'
    r'(?:(\(\(|\[\[|\[\(|\(\[|\[|\(|\{)\s*"?(.*?)"?\s*(\)\)|\]\]|\)\]|\]\)|\]|\)|\}))?\s*$')
_CLASS_TOKEN = re.compile(r'@([^\s,|]+)')
_CONFIDENCE = re.compile(r'^%%\s*minimumDetectionConfidence\s*:\s*([0-9.]+)\s*$')
_HEADING = re.compile(r'^#\s+(.+?)\s*$')

_SKIP_PREFIXES = ("%%", "classDef", "class ", "style ", "linkStyle", "direction", "subgraph")


class _Node:
    def __init__(self, node_id: str, order: int) -> None:
        self.id = node_id
        self.order = order
        self.title = ""
        self.description = ""
        self.detected_class = ""
        self.label = ""
        self.model_ref = ""
        self.image_ref = ""
        self.explicit_waiting: Optional[str] = None


class _Edge:
    def __init__(self, src: str, dst: str, options: List[str], result_class: Optional[str]) -> None:
        self.src = src
        self.dst = dst
        self.options = options
        self.result_class = result_class


def try_parse_markdown(text: Optional[str]) -> Tuple[bool, Optional[TrainingScenario], Optional[float], ValidationReport]:
    """Parse a markdown+mermaid training document. Returns
    ``(ok, scenario, minimum_detection_confidence, report)`` — ``ok`` is False
    (scenario ``None``) when the report contains errors."""
    report = ValidationReport()

    if not text:
        report.error("Empty document.")
        return False, None, None, report

    lines = text.splitlines()
    name = ""
    for line in lines:
        heading = _HEADING.match(line.strip())
        if heading:
            name = heading.group(1)
            break

    block = _extract_mermaid_block(lines)
    if block is None:
        report.error("No ```mermaid code block found in the document.")
        return False, None, None, report

    confidence: Optional[float] = None
    nodes: Dict[str, _Node] = {}
    order: List[str] = []
    edges: List[_Edge] = []
    saw_header = False

    def touch(node_id: str, node_text: Optional[str]) -> None:
        key = node_id.casefold()
        if key == END_NODE_ID.casefold():
            return
        if node_id not in nodes:
            nodes[node_id] = _Node(node_id, len(order))
            order.append(node_id)
        if node_text is not None and node_text.strip():
            _apply_text(nodes[node_id], node_text)

    for raw in block:
        line = raw.strip()
        if not line:
            continue

        conf_match = _CONFIDENCE.match(line)
        if conf_match:
            try:
                confidence = float(conf_match.group(1))
            except ValueError:
                report.error(f"Invalid minimumDetectionConfidence value: '{conf_match.group(1)}'.")
            continue

        if line.startswith(_SKIP_PREFIXES) or line == "end":
            continue

        if not saw_header:
            if line.startswith("flowchart") or line.startswith("graph"):
                saw_header = True
                continue
            report.error(f"The mermaid block must start with 'flowchart' (got: '{line}').")
            return False, None, None, report

        if "-->" in line:
            _parse_edge_line(line, touch, edges, report)
        else:
            token = _NODE_TOKEN.match(line)
            if token:
                touch(token.group(1), token.group(3))
            else:
                report.error(f"Unrecognised mermaid line: '{line}'.")

    if report.has_errors:
        return False, None, None, report

    if not order:
        report.error("The mermaid block defines no step nodes.")
        return False, None, None, report

    steps = _build_steps(nodes, order, edges, report)
    if report.has_errors:
        return False, None, None, report

    scenario = TrainingScenario(name=name, steps=tuple(steps))
    validate_scenario(scenario, report)
    return True, scenario, confidence, report


def to_markdown(scenario: TrainingScenario, minimum_detection_confidence: Optional[float] = None) -> str:
    """Generate the markdown+mermaid training document for a scenario: the
    diagram (round-trippable through :func:`try_parse_markdown`) plus a
    human-readable verification table."""
    steps = scenario.steps
    ids = [f"s{i + 1}" for i in range(len(steps))]

    # Mirror the machine's forward-only offer: each result targets the FIRST
    # later step waiting on it.
    targets: List[Optional[int]] = []
    for i, step in enumerate(steps):
        target = None
        if step.result:
            for j in range(i + 1, len(steps)):
                if steps[j].waiting_class.casefold() == step.result.casefold():
                    target = j
                    break
        targets.append(target)
    reached = {t for t in targets if t is not None}

    lines: List[str] = []
    title = scenario.name if scenario.name else "Training Scenario"
    lines.append(f"# {title}")
    lines.append("")
    lines.append("> Generated by the training builder — edit and re-import; the diagram is")
    lines.append("> the configuration. Dialect: `Documentation/Training-Builder.md`.")
    lines.append("")
    lines.append("```mermaid")
    lines.append("flowchart TD")
    if minimum_detection_confidence is not None:
        lines.append(f"  %% minimumDetectionConfidence: {_format_number(minimum_detection_confidence)}")

    for i, step in enumerate(steps):
        segments: List[str] = []
        segments.append(_escape(step.title))
        if step.description:
            segments.append(_escape(step.description))
        if step.detected_class:
            segments.append(f"@{step.detected_class}: {_escape(step.label)}" if step.label else f"@{step.detected_class}")
        if step.model_ref:
            segments.append(f"#{step.model_ref}")
        if step.image_ref:
            segments.append(f"!{step.image_ref}")
        needs_waiting = step.waiting_class and (i == 0 or i not in reached)
        if needs_waiting:
            segments.append(f"?{step.waiting_class}")
        text = "|".join(segments)
        lines.append(f'  {ids[i]}["{text if text.strip(" |") else " "}"]')

    lines.append(f"  {END_NODE_ID}([Scenario complete])")

    for i, step in enumerate(steps):
        label_parts: List[str] = []
        if step.options:
            label_parts.append(", ".join(step.options))
        if step.result:
            label_parts.append(f"@{step.result}")
        label = " ".join(label_parts)
        dst = ids[targets[i]] if targets[i] is not None else END_NODE_ID
        if not step.result and i == len(steps) - 1 and not step.options:
            label = label or ""
        arrow = f"  {ids[i]} -->|{label}| {dst}" if label else f"  {ids[i]} --> {dst}"
        lines.append(arrow)

    lines.append("```")
    lines.append("")
    lines.append("## Steps")
    lines.append("")
    lines.append("| # | Step | Waits for | Options | Result | World label | Model | Image |")
    lines.append("|---|------|-----------|---------|--------|-------------|-------|-------|")
    for i, step in enumerate(steps):
        world = f"`{step.detected_class}`: {step.label}" if step.has_world_label else (
            f"`{step.detected_class}`" if step.detected_class else "")
        lines.append(
            f"| {i + 1} "
            f"| {step.title if step.title else '*(pass-through)*'} "
            f"| {f'`{step.waiting_class}`' if step.waiting_class else '*(begin)*'} "
            f"| {', '.join(step.options)} "
            f"| {f'`{step.result}`' if step.result else '*(complete)*'} "
            f"| {world} "
            f"| {f'`{step.model_ref}`' if step.model_ref else ''} "
            f"| {f'`{step.image_ref}`' if step.image_ref else ''} |")
    lines.append("")
    return "\n".join(lines)


# ------------------------------------------------------------------ internals

def _extract_mermaid_block(lines: List[str]) -> Optional[List[str]]:
    block: List[str] = []
    inside = False
    for line in lines:
        stripped = line.strip()
        if not inside and stripped.startswith("```mermaid"):
            inside = True
            continue
        if inside:
            if stripped.startswith("```"):
                return block
            block.append(line)
    return None if not inside else block


def _apply_text(node: _Node, text: str) -> None:
    plain: List[str] = []
    for segment in (s.strip() for s in text.split("|")):
        if not segment:
            # Positional: an empty leading segment keeps the title empty while a
            # later segment carries the description.
            plain.append("")
            continue
        if segment.startswith("@"):
            match = re.match(r'@([^\s:]+)\s*:?\s*(.*)$', segment)
            if match:
                node.detected_class = match.group(1)
                node.label = match.group(2).strip()
        elif segment.startswith("#"):
            node.model_ref = segment[1:].strip()
        elif segment.startswith("!"):
            node.image_ref = segment[1:].strip()
        elif segment.startswith("?"):
            node.explicit_waiting = segment[1:].strip()
        else:
            plain.append(segment)
    if plain:
        node.title = plain[0]
        node.description = " ".join(p for p in plain[1:] if p) if len(plain) > 1 else ""


def _parse_edge_line(line: str, touch, edges: List[_Edge], report: ValidationReport) -> None:
    parts = re.split(r'\s*-->\s*', line)
    left = _NODE_TOKEN.match(parts[0].strip())
    if not left:
        report.error(f"Unrecognised edge source in: '{line}'.")
        return
    touch(left.group(1), left.group(3))
    prev = left.group(1)

    for part in parts[1:]:
        part = part.strip()
        label: Optional[str] = None
        if part.startswith("|"):
            close = part.find("|", 1)
            if close < 0:
                report.error(f"Unterminated edge label in: '{line}'.")
                return
            label = part[1:close]
            part = part[close + 1:].strip()

        node = _NODE_TOKEN.match(part)
        if not node:
            report.error(f"Unrecognised edge target in: '{line}'.")
            return
        touch(node.group(1), node.group(3))

        options, result_class = _parse_label(label, report)
        edges.append(_Edge(prev, node.group(1), options, result_class))
        prev = node.group(1)


def _parse_label(label: Optional[str], report: ValidationReport) -> Tuple[List[str], Optional[str]]:
    if label is None or not label.strip():
        return [], None
    classes = _CLASS_TOKEN.findall(label)
    if len(classes) > 1:
        report.error(f"An edge label may carry at most one @class token (got: '{label}').")
    remainder = _CLASS_TOKEN.sub("", label).strip()
    options = [o.strip() for o in remainder.split(",") if o.strip()]
    return options, classes[0] if classes else None


def _build_steps(nodes: Dict[str, _Node], order: List[str], edges: List[_Edge],
                 report: ValidationReport) -> List[TrainingStep]:
    is_end = lambda node_id: node_id.casefold() == END_NODE_ID.casefold()
    steps: List[TrainingStep] = []

    for index, node_id in enumerate(order):
        node = nodes[node_id]

        # --- outgoing: options + result -------------------------------------
        outgoing = [e for e in edges if e.src == node_id]
        options: List[str] = []
        result = ""
        dst_seen: Optional[str] = None
        result_seen: Optional[str] = None
        for edge in outgoing:
            if dst_seen is not None and edge.dst != dst_seen:
                report.error(
                    f"Node '{node_id}' branches to both '{dst_seen}' and '{edge.dst}' — the "
                    "current linear model supports one target per step (per-option results "
                    "are a planned schema extension).")
                continue
            dst_seen = edge.dst
            options.extend(edge.options)
            edge_result = edge.result_class if edge.result_class is not None else (
                "" if is_end(edge.dst) else edge.dst.casefold())
            if result_seen is not None and edge_result.casefold() != result_seen.casefold():
                report.error(
                    f"Node '{node_id}' has edges with conflicting result classes "
                    f"('{result_seen}' vs '{edge_result}').")
            result_seen = edge_result
        result = result_seen if result_seen is not None else ""

        # --- incoming: waiting class ----------------------------------------
        if node.explicit_waiting is not None:
            waiting = node.explicit_waiting
        elif index == 0:
            waiting = ""
        else:
            incoming = [e for e in edges if e.dst == node_id]
            waiting = ""
            for edge in incoming:
                edge_class = edge.result_class if edge.result_class is not None else node_id.casefold()
                if waiting and edge_class.casefold() != waiting.casefold():
                    report.error(
                        f"Node '{node_id}' is reached by edges carrying different classes "
                        f"('{waiting}' vs '{edge_class}') — merge nodes must agree; use "
                        "?waitingClass to override.")
                waiting = waiting or edge_class

        steps.append(TrainingStep(
            waiting_class=waiting,
            title=node.title,
            description=node.description,
            options=tuple(options),
            detected_class=node.detected_class,
            label=node.label,
            image_ref=node.image_ref,
            result=result,
            model_ref=node.model_ref,
        ))

    return steps


def _escape(text: str) -> str:
    return text.replace('"', "'").replace("|", "/")


def _format_number(value: float) -> str:
    text = f"{value:.4f}".rstrip("0").rstrip(".")
    return text if text else "0"
