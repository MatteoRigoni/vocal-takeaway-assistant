# Voice Ordering Feature

This document describes the implementation of the voice-enabled ordering workflow that now ships with the Vocal Takeaway Assistant frontend. It highlights the moving parts, the technologies they rely on, and how they collaborate to capture speech, send it to the backend, and play synthesized confirmations back to the crew.

## High-level flow

1. **Capture** – The `VoiceOrderComponent` requests microphone access through the Web Audio API (`navigator.mediaDevices.getUserMedia`) and streams raw audio chunks with `MediaRecorder`.
2. **Visualise** – Live wave activity is sampled via `AudioContext` + `AnalyserNode` to show recording feedback while the user speaks.
3. **Transcribe** – Recorded audio buffers are encoded to Base64 and POSTed to `/api/voice/session`. The endpoint responds with the recognized transcript, which the `VoiceService` publishes for UI consumption.
4. **Confirm** – When the UI requests audible confirmation, the same `/api/voice/session` endpoint is called with the desired response text so the backend can return synthesized speech chunks that the Web Audio API plays back.

> ℹ️  A dedicated SignalR voice hub is not yet available. The `VoiceService` keeps the streaming hooks in place but currently mirrors the latest recognized transcript instead of opening a websocket connection.

## Technologies leveraged

| Layer | Technology | Purpose |
| --- | --- | --- |
| Component UI | Angular standalone components, template control flow (`ngIf`, `ngSwitch`, `NgClass`) | Build the voice ordering screen without NgModules and keep state reactive using Angular Signals. |
| Audio capture & playback | Web Audio API (`MediaStream`, `MediaRecorder`, `AudioContext`, `AnalyserNode`) | Request mic permissions, gather waveform data, animate levels, and play synthesized responses. |
| Networking | Angular `HttpClient`, Fetch API backend bridge | Exchange JSON payloads with `/api/voice/session` for transcription and synthesis. |
| State sharing | RxJS (`BehaviorSubject`, `ReplaySubject`, `Observable`) | Expose recognized text and synthesized audio to any consumer while keeping emissions testable. |
| Testing | Jasmine, Angular TestBed, HttpClientTestingModule | Mock browser APIs (MediaRecorder, AudioContext), assert permission handling, REST calls, and playback orchestration. |

## File overview

- `frontend/src/app/voice/voice-order.component.ts|html|css` – Standalone Angular component controlling the voice UI, microphone lifecycle, waveform animation, and playback prompts.
- `frontend/src/app/voice/voice.service.ts` – Reusable service that prepares session payloads, calls the `/api/voice/session` endpoint, and handles AudioContext playback helpers.
- `frontend/src/app/app.routes.ts` / `app.html` / `app.css` – Router and shell updates so `/voice` is reachable from navigation without disturbing the existing KDS view.
- `frontend/src/app/voice/*.spec.ts` – Jasmine specs covering microphone permission fallbacks, mocked backend contracts, and Web Audio playback integration.

## Running the tests

```bash
cd frontend
npm install
npm test
```

The suite provisions browser API shims (MediaRecorder, AudioContext, navigator.mediaDevices) to validate the workflow without a physical microphone or audio hardware.
