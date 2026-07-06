# Keep the WebRTC and plugin classes reachable from JNI / reflection.
-keep class org.webrtc.** { *; }
-keep class com.questvisionstream.** { *; }
