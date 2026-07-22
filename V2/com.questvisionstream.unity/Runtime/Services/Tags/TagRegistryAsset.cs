// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>A registered tag: the single source of truth shared with the print generator.</summary>
    [Serializable]
    public struct TagDefinition
    {
        public int Id;
        public string Name;
        public Color Color;

        [Tooltip("Detection-pipeline class token this tag stands for ('tv', 'station1', …) — what the training flow matches against. Empty falls back to Name.")]
        public string ClassName;

        /// <summary>
        /// The class token this tag emits on the detection pipeline:
        /// <see cref="ClassName"/> when set, otherwise <see cref="Name"/>. This is
        /// the value the training flow's <c>waitingClass</c>/<c>detectedClass</c>
        /// steps compare against (case-insensitive).
        /// </summary>
        public string EffectiveClassName => string.IsNullOrEmpty(ClassName) ? Name : ClassName;
    }

    /// <summary>
    /// The known-tag registry, mirroring <c>V2/quest-client/src/apriltags/tag-registry.json</c>
    /// (ids 0–9, Alpha…Juliet). Keep in sync with <c>V2/tools/generate-apriltags.py</c> —
    /// the printed tags, the registry and the detector dictionary must agree.
    /// </summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Tag Registry", fileName = "TagRegistry")]
    public class TagRegistryAsset : ScriptableObject
    {
        [SerializeField]
        private List<TagDefinition> tags = new List<TagDefinition>();

        public IReadOnlyList<TagDefinition> Tags => tags;

        public bool IsKnown(int id)
        {
            foreach (var tag in tags)
            {
                if (tag.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Registry entry for <paramref name="id"/>, or a white "Tag N" fallback.</summary>
        public TagDefinition GetInfo(int id)
        {
            foreach (var tag in tags)
            {
                if (tag.Id == id)
                {
                    return tag;
                }
            }

            return new TagDefinition { Id = id, Name = $"Tag {id}", Color = Color.white };
        }

        /// <summary>The default registry — ids 0–9, Alpha…Juliet, iOS-palette colours.</summary>
        public static TagRegistryAsset CreateDefault()
        {
            var asset = CreateInstance<TagRegistryAsset>();
            asset.tags = new List<TagDefinition>
            {
                new TagDefinition { Id = 0, Name = "Alpha", Color = FromHex(0xff3b30) },
                new TagDefinition { Id = 1, Name = "Bravo", Color = FromHex(0xff9500) },
                new TagDefinition { Id = 2, Name = "Charlie", Color = FromHex(0xffcc00) },
                new TagDefinition { Id = 3, Name = "Delta", Color = FromHex(0x34c759) },
                new TagDefinition { Id = 4, Name = "Echo", Color = FromHex(0x00c7be) },
                new TagDefinition { Id = 5, Name = "Foxtrot", Color = FromHex(0x30b0c7) },
                new TagDefinition { Id = 6, Name = "Golf", Color = FromHex(0x007aff) },
                new TagDefinition { Id = 7, Name = "Hotel", Color = FromHex(0x5856d6) },
                new TagDefinition { Id = 8, Name = "India", Color = FromHex(0xaf52de) },
                new TagDefinition { Id = 9, Name = "Juliet", Color = FromHex(0xff2d92) }
            };
            return asset;
        }

        private static Color FromHex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }
}
