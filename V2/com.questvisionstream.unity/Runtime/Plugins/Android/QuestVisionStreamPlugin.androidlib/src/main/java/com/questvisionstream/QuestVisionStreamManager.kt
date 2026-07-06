package com.questvisionstream

import android.app.Activity
import android.util.Log
import org.json.JSONObject
import org.webrtc.*
import com.questvisionstream.capture.PixelDataVideoCapturer
import com.questvisionstream.util.Channels
import com.questvisionstream.util.EglUtils
import com.questvisionstream.util.PeerConnectionFactoryProvider
import com.questvisionstream.util.UnityBridge
import java.nio.ByteBuffer.wrap

/**
 * V2 of the QuestVisionStream plugin: MEDIA ONLY.
 *
 * Signaling lives in C# (`SignalingService` over com.utilities.websockets); this
 * class owns the PeerConnection, the pixel-fed video track and the `detections`
 * data channel. All communication back to Unity flows through ONE JSON event
 * pipe: `UnitySendMessage(callbackGameObject, "OnQvsEvent", json)` with an
 * `event` discriminator — see [emit].
 *
 * Session lifecycle: [startSession] (repeatable — tears down any prior session),
 * [setRemoteAnswer]/[addRemoteCandidate] from C# signaling, [closeSession].
 */
class QuestVisionStreamManager(
    private val activity: Activity,
    private val callbackGameObject: String
) {
    private lateinit var peerConnectionFactory: PeerConnectionFactory
    private var eglBase: EglBase? = null
    private var peerConnection: PeerConnection? = null
    private var videoSource: VideoSource? = null
    private var videoTrack: VideoTrack? = null
    private var dataChannel: DataChannel? = null
    private var pixelDataCapturer: PixelDataVideoCapturer? = null
    private var iceServers: MutableList<PeerConnection.IceServer> = mutableListOf()
    private var targetFps: Int = 30

    init {
        try {
            System.loadLibrary("jingle_peerconnection_so")
            Log.i(TAG, "WebRTC native library loaded")
        } catch (e: UnsatisfiedLinkError) {
            Log.e(TAG, "Failed to load WebRTC library: ${e.message}")
        }
        eglBase = EglUtils.eglBase
        peerConnectionFactory = PeerConnectionFactoryProvider.create(activity, eglBase!!)
        Log.i(TAG, "PeerConnectionFactory initialized (callback target: $callbackGameObject)")
    }

    // region Configuration

    fun setIceServers(servers: List<String>) {
        iceServers.clear()
        for (url in servers) {
            try {
                iceServers.add(PeerConnection.IceServer.builder(url).createIceServer())
                Log.i(TAG, "ICE server added: $url")
            } catch (e: Exception) {
                Log.e(TAG, "Invalid ICE server: $url", e)
            }
        }
    }

    fun addTurnServer(url: String, username: String, credential: String) {
        try {
            iceServers.add(
                PeerConnection.IceServer.builder(url)
                    .setUsername(username)
                    .setPassword(credential)
                    .createIceServer()
            )
            Log.i(TAG, "TURN server added: $url")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to add TURN server: $url", e)
        }
    }

    fun setTargetFps(fps: Int) {
        targetFps = fps
        pixelDataCapturer?.setTargetFps(fps)
    }

    // endregion

    // region Session lifecycle

    /**
     * Create a fresh session: capturer + video track + PeerConnection + the
     * `detections` data channel (client is offerer and creates the channel; the
     * server only listens), then produce an offer, emitted as `localOffer`.
     * Any existing session is torn down first, so this doubles as renegotiation.
     */
    fun startSession(width: Int, height: Int, fps: Int) {
        closeSessionInternal()
        targetFps = fps
        Log.i(TAG, "Starting session ${width}x$height @${fps}fps with ${iceServers.size} ICE servers")

        val source = peerConnectionFactory.createVideoSource(false)
        videoSource = source
        val capturer = PixelDataVideoCapturer(width, height, fps)
        capturer.initializePixelCapture(activity, source.capturerObserver)
        capturer.startCapture(width, height, fps)
        pixelDataCapturer = capturer
        videoTrack = peerConnectionFactory.createVideoTrack(TRACK_ID, source)

        val rtcConfig = PeerConnection.RTCConfiguration(iceServers).apply {
            sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
        }

        val pc = peerConnectionFactory.createPeerConnection(rtcConfig, object : PeerConnection.Observer {
            override fun onIceCandidate(candidate: IceCandidate) {
                emit(JSONObject().apply {
                    put("event", "localCandidate")
                    put("candidate", candidate.sdp)
                    put("sdpMid", candidate.sdpMid)
                    put("sdpMLineIndex", candidate.sdpMLineIndex)
                })
            }

            override fun onConnectionChange(newState: PeerConnection.PeerConnectionState) {
                Log.i(TAG, "PeerConnection state: $newState")
                emit(JSONObject().apply {
                    put("event", "pcState")
                    put("state", newState.name)
                })
            }

            override fun onDataChannel(channel: DataChannel) {
                // Server-created channels (none expected today, but register anyway).
                Log.i(TAG, "Remote DataChannel: ${channel.label()}")
                registerDataChannel(channel)
            }

            override fun onIceGatheringChange(newState: PeerConnection.IceGatheringState) {
                Log.i(TAG, "ICE gathering: $newState")
            }

            override fun onIceConnectionChange(newState: PeerConnection.IceConnectionState) {
                Log.i(TAG, "ICE connection: $newState")
                emit(JSONObject().apply {
                    put("event", "iceState")
                    put("state", newState.name)
                })
            }

            override fun onSignalingChange(newState: PeerConnection.SignalingState) {}
            override fun onRenegotiationNeeded() {}
            override fun onTrack(transceiver: RtpTransceiver) {}
            override fun onAddStream(stream: MediaStream) {}
            override fun onRemoveStream(stream: MediaStream) {}
            override fun onIceCandidatesRemoved(candidates: Array<IceCandidate>) {}
            override fun onStandardizedIceConnectionChange(newState: PeerConnection.IceConnectionState) {}
            override fun onIceConnectionReceivingChange(receiving: Boolean) {}
        })

        if (pc == null) {
            emitError("createPeerConnection returned null")
            return
        }
        peerConnection = pc

        pc.addTrack(videoTrack, listOf(STREAM_ID))
        registerDataChannel(pc.createDataChannel(Channels.DETECTIONS, DataChannel.Init()))
        createOffer(pc)
    }

    fun setRemoteAnswer(sdp: String) {
        val pc = peerConnection ?: run {
            Log.w(TAG, "setRemoteAnswer with no session")
            return
        }
        Log.i(TAG, "Applying remote answer")
        pc.setRemoteDescription(object : SdpObserver {
            override fun onSetSuccess() {
                Log.i(TAG, "Remote answer applied")
            }

            override fun onSetFailure(error: String) = emitError("setRemoteDescription failed: $error")
            override fun onCreateSuccess(desc: SessionDescription?) {}
            override fun onCreateFailure(error: String?) {}
        }, SessionDescription(SessionDescription.Type.ANSWER, sdp))
    }

    fun addRemoteCandidate(candidate: String, sdpMid: String, sdpMLineIndex: Int) {
        val pc = peerConnection ?: return
        pc.addIceCandidate(IceCandidate(sdpMid, sdpMLineIndex, candidate))
    }

    fun closeSession() {
        Log.i(TAG, "Closing session")
        closeSessionInternal()
    }

    // endregion

    // region Frames + data channel

    fun updateFrameData(pixelData: ByteArray, width: Int, height: Int) {
        pixelDataCapturer?.updateFrame(pixelData, width, height)
    }

    fun updateFrameDataYUV(yData: ByteArray, uData: ByteArray, vData: ByteArray, width: Int, height: Int) {
        pixelDataCapturer?.updateFrameYUV(yData, uData, vData, width, height)
    }

    fun sendDataChannelMessage(message: String) {
        val channel = dataChannel
        if (channel == null || channel.state() != DataChannel.State.OPEN) {
            Log.w(TAG, "DataChannel not open; cannot send")
            return
        }
        channel.send(DataChannel.Buffer(wrap(message.toByteArray(Charsets.UTF_8)), false))
    }

    // endregion

    private fun createOffer(pc: PeerConnection) {
        pc.createOffer(object : SdpObserver {
            override fun onCreateSuccess(desc: SessionDescription) {
                pc.setLocalDescription(this, desc)
                emit(JSONObject().apply {
                    put("event", "localOffer")
                    put("sdp", desc.description)
                })
            }

            override fun onCreateFailure(error: String) = emitError("createOffer failed: $error")
            override fun onSetSuccess() {}
            override fun onSetFailure(error: String) = emitError("setLocalDescription failed: $error")
        }, MediaConstraints())
    }

    private fun registerDataChannel(channel: DataChannel) {
        dataChannel = channel
        channel.registerObserver(object : DataChannel.Observer {
            override fun onBufferedAmountChange(previousAmount: Long) {}

            override fun onStateChange() {
                emit(JSONObject().apply {
                    put("event", "dcState")
                    put("label", channel.label())
                    put("state", channel.state().name)
                })
            }

            override fun onMessage(buffer: DataChannel.Buffer) {
                if (buffer.binary) return
                try {
                    val data = ByteArray(buffer.data.remaining())
                    buffer.data.get(data)
                    emit(JSONObject().apply {
                        put("event", "dcMessage")
                        put("label", channel.label())
                        put("message", String(data, Charsets.UTF_8))
                    })
                } catch (e: Exception) {
                    Log.e(TAG, "DataChannel message handling failed", e)
                }
            }
        })
    }

    private fun closeSessionInternal() {
        try {
            pixelDataCapturer?.stopCapture()
            pixelDataCapturer?.dispose()
        } catch (e: Exception) {
            Log.w(TAG, "Capturer teardown: ${e.message}")
        }
        pixelDataCapturer = null

        try {
            dataChannel?.close()
        } catch (_: Exception) {
        }
        dataChannel = null

        try {
            videoTrack?.dispose()
        } catch (_: Exception) {
        }
        videoTrack = null

        try {
            videoSource?.dispose()
        } catch (_: Exception) {
        }
        videoSource = null

        try {
            peerConnection?.close()
        } catch (_: Exception) {
        }
        peerConnection = null
    }

    private fun emitError(message: String) {
        Log.e(TAG, message)
        emit(JSONObject().apply {
            put("event", "error")
            put("message", message)
        })
    }

    /** UnitySendMessage is thread-safe (it queues onto the Unity main thread). */
    private fun emit(json: JSONObject) = UnityBridge.send(callbackGameObject, UNITY_METHOD, json.toString())

    private companion object {
        const val TAG = "QuestVisionStream"
        const val UNITY_METHOD = "OnQvsEvent"
        const val TRACK_ID = "ARDAMSv0"
        const val STREAM_ID = "ARDAMS"
    }
}
