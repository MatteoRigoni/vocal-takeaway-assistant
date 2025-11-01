# Voice Ordering Feature

This document describes the implementation of the voice-enabled ordering workflow that now ships with the Vocal Takeaway Assistant frontend. It highlights the moving parts, the technologies they rely on, and how they collaborate to capture speech, send it to the backend, and play synthesized confirmations back to the crew.

## High-level flow

1. **Capture** – The `VoiceOrderComponent` requests microphone access through the Web Audio API (`navigator.mediaDevices.getUserMedia`) and streams raw audio chunks with `MediaRecorder`.
2. **Visualise** – Live wave activity is sampled via `AudioContext` + `AnalyserNode` to show recording feedback while the user speaks.
3. **Transcribe** – Recorded audio buffers are pushed to the backend `/api/speech/stt` endpoint using Angular's `HttpClient`. The response text is published through the new `VoiceService` for display.
4. **Confirm** – The recognized transcript is sent back to `/api/speech/tts`, returning a speech synthesis blob that is decoded and rendered with the Web Audio API for audible confirmation.
5. **Stream (optional)** – When SignalR streaming is available, the service opens a Hub connection (`@microsoft/signalr`) so partial transcripts can appear in real time.

## Technologies leveraged

| Layer | Technology | Purpose |
| --- | --- | --- |
| Component UI | Angular standalone components, template control flow (`ngIf`, `ngSwitch`, `NgClass`) | Build the voice ordering screen without NgModules and keep state reactive using Angular Signals. |
| Audio capture & playback | Web Audio API (`MediaStream`, `MediaRecorder`, `AudioContext`, `AnalyserNode`) | Request mic permissions, gather waveform data, animate levels, and play synthesized responses. |
| Networking | Angular `HttpClient`, Fetch API backend bridge | Upload audio blobs for STT and download blob responses for TTS. |
| Realtime updates | `@microsoft/signalr` HubConnection | Provide optional streaming transcripts from the backend voice hub. |
| State sharing | RxJS (`BehaviorSubject`, `ReplaySubject`, `Observable`) | Expose recognized text and synthesized audio to any consumer while keeping emissions testable. |
| Testing | Jasmine, Angular TestBed, HttpClientTestingModule | Mock browser APIs (MediaRecorder, AudioContext), assert permission handling, REST calls, and playback orchestration. |

## File overview

- `frontend/src/app/voice/voice-order.component.ts|html|css` – Standalone Angular component controlling the voice UI, microphone lifecycle, waveform animation, and playback prompts.
- `frontend/src/app/voice/voice.service.ts` – Reusable service that wraps STT/TTS REST calls, optional SignalR streaming, and AudioContext playback helpers.
- `frontend/src/app/app.routes.ts` / `app.html` / `app.css` – Router and shell updates so `/voice` is reachable from navigation without disturbing the existing KDS view.
- `frontend/src/app/voice/*.spec.ts` – Jasmine specs covering microphone permission fallbacks, mocked backend contracts, and Web Audio playback integration.

## Running the tests

```bash
cd frontend
npm install
npm test
```

The suite provisions browser API shims (MediaRecorder, AudioContext, navigator.mediaDevices) to validate the workflow without a physical microphone or audio hardware.
