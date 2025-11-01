import { Injectable, NgZone } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { BehaviorSubject, Observable, ReplaySubject, Subject, defer, map, switchAll, tap } from 'rxjs';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

interface TranscriptionResponse {
  text: string;
  confidence?: number;
}

@Injectable({ providedIn: 'root' })
export class VoiceService {
  private readonly sttEndpoint = '/api/speech/stt';
  private readonly ttsEndpoint = '/api/speech/tts';
  private readonly voiceHubUrl = '/hubs/voice';

  private readonly recognizedTextSubject = new BehaviorSubject<string>('');
  readonly recognizedText$ = this.recognizedTextSubject.asObservable();

  private readonly synthesizedAudioSubject = new ReplaySubject<Blob>(1);
  readonly synthesizedAudio$ = this.synthesizedAudioSubject.asObservable();

  private hubConnection?: HubConnection;
  private audioContext?: AudioContext;

  constructor(private readonly http: HttpClient, private readonly zone: NgZone) {}

  transcribeAudio(audioBlob: Blob): Observable<string> {
    const formData = new FormData();
    formData.append('file', audioBlob, 'voice-command.webm');

    return this.http.post<TranscriptionResponse>(this.sttEndpoint, formData).pipe(
      map((response) => response?.text ?? ''),
      tap((text) => this.recognizedTextSubject.next(text))
    );
  }

  requestSynthesis(text: string): Observable<Blob> {
    const headers = new HttpHeaders({ 'Content-Type': 'application/json' });
    return this.http
      .post(this.ttsEndpoint, { text }, { headers, responseType: 'blob' })
      .pipe(tap((blob) => this.synthesizedAudioSubject.next(blob)));
  }

  async playAudio(blob: Blob): Promise<void> {
    const arrayBuffer = await blob.arrayBuffer();
    this.audioContext ??= new AudioContext();
    const audioBuffer = await this.audioContext.decodeAudioData(arrayBuffer.slice(0));
    const source = this.audioContext.createBufferSource();
    source.buffer = audioBuffer;
    source.connect(this.audioContext.destination);
    return new Promise((resolve) => {
      source.onended = () => resolve();
      source.start(0);
    });
  }

  connectToVoiceStream(): Observable<string> {
    if (this.hubConnection) {
      return this.recognizedText$;
    }

    const connection$ = defer(async () => {
      this.hubConnection = new HubConnectionBuilder()
        .withUrl(this.voiceHubUrl)
        .configureLogging(LogLevel.Information)
        .withAutomaticReconnect()
        .build();

      const transcriptUpdates = new Subject<string>();

      this.hubConnection.on('TranscriptionUpdated', (text: string) => {
        this.zone.run(() => {
          this.recognizedTextSubject.next(text);
          transcriptUpdates.next(text);
        });
      });

      this.hubConnection.onclose(() => {
        transcriptUpdates.complete();
      });

      await this.hubConnection.start();
      return transcriptUpdates.asObservable();
    });

    return connection$.pipe(switchAll(), tap({ error: () => this.teardownHub() }));
  }

  disconnectStream(): void {
    this.teardownHub();
  }

  private teardownHub(): void {
    if (this.hubConnection) {
      void this.hubConnection.stop();
      this.hubConnection = undefined;
    }
  }
}
