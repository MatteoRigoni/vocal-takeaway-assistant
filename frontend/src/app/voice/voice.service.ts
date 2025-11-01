import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, Subject, from, switchMap, tap } from 'rxjs';

interface VoiceSessionResponse {
  recognizedText: string;
  responseAudioChunks: string[];
  promptText?: string | null;
  dialogState?: string | null;
  isSessionComplete?: boolean | null;
  metadata?: Record<string, string> | null;
}

export type TranscriptSpeaker = 'user' | 'assistant';

export interface TranscriptEntry {
  speaker: TranscriptSpeaker;
  text: string;
}

@Injectable({ providedIn: 'root' })
export class VoiceService {
  private readonly voiceSessionEndpoint = '/api/voice/session';

  private readonly recognizedTextSubject = new BehaviorSubject<string>('');
  readonly recognizedText$ = this.recognizedTextSubject.asObservable();

  private readonly promptTextSubject = new BehaviorSubject<string | null>(null);
  readonly promptText$ = this.promptTextSubject.asObservable();

  private readonly dialogStateSubject = new BehaviorSubject<string | null>(null);
  readonly dialogState$ = this.dialogStateSubject.asObservable();

  private readonly sessionCompleteSubject = new BehaviorSubject<boolean>(false);
  readonly sessionComplete$ = this.sessionCompleteSubject.asObservable();

  private readonly metadataSubject = new BehaviorSubject<Record<string, string> | null>(null);
  readonly metadata$ = this.metadataSubject.asObservable();

  private readonly transcriptEntriesSubject = new BehaviorSubject<TranscriptEntry[]>([]);
  readonly transcriptEntries$ = this.transcriptEntriesSubject.asObservable();

  private readonly synthesizedAudioSubject = new Subject<Blob>();
  readonly synthesizedAudio$ = this.synthesizedAudioSubject.asObservable();

  private audioContext?: AudioContext;

  constructor(private readonly http: HttpClient) {}

  transcribeAudio(audioBlob: Blob): Observable<VoiceSessionResponse> {
    return from(this.encodeAudioBlob(audioBlob)).pipe(
      switchMap((audioChunks) =>
        this.http.post<VoiceSessionResponse>(this.voiceSessionEndpoint, {
          audioChunks,
          utteranceText: null,
          voice: null,
          CallerId: 'TestUser',
        })
      ),
      tap((response) => {
        const recognized = response?.recognizedText ? response.recognizedText.trim() : '';
        this.recognizedTextSubject.next(recognized);
        if (recognized) {
          this.appendTranscriptEntry('user', recognized);
        }

        const prompt = response?.promptText ? response.promptText.trim() : '';
        this.promptTextSubject.next(prompt || null);
        if (prompt) {
          this.appendTranscriptEntry('assistant', prompt);
        }

        this.dialogStateSubject.next(response?.dialogState ?? null);
        this.sessionCompleteSubject.next(Boolean(response?.isSessionComplete));
        this.metadataSubject.next(response?.metadata ?? null);

        const audioBlob = this.decodeAudioChunks(response?.responseAudioChunks ?? []);
        if (audioBlob.size > 0) {
          this.synthesizedAudioSubject.next(audioBlob);
        }
      })
    );
  }

  createAudioBlob(chunks: Iterable<string> | null | undefined, mimeType = 'audio/wav'): Blob {
    return this.decodeAudioChunks(Array.from(chunks ?? []), mimeType);
  }

  async playAudio(blob: Blob): Promise<void> {
    console.log('[VoiceService] playAudio: ricevo blob da riprodurre:', blob);
    const arrayBuffer = await blob.arrayBuffer();
    console.log('[VoiceService] ArrayBuffer byteLength:', arrayBuffer.byteLength);
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
    return this.recognizedText$;
  }

  disconnectStream(): void {
    // No streaming hub is currently available; method retained for API compatibility.
  }

  private appendTranscriptEntry(speaker: TranscriptSpeaker, text: string): void {
    const current = this.transcriptEntriesSubject.getValue();
    this.transcriptEntriesSubject.next([...current, { speaker, text }]);
  }

  private async encodeAudioBlob(blob: Blob, chunkSize = 16384): Promise<string[]> {
    const buffer = await blob.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    if (!bytes.length) {
      return [];
    }

    const encoded: string[] = [];
    for (let offset = 0; offset < bytes.length; offset += chunkSize) {
      const slice = bytes.subarray(offset, offset + chunkSize);
      let binary = '';
      for (let index = 0; index < slice.length; index++) {
        binary += String.fromCharCode(slice[index]);
      }
      encoded.push(btoa(binary));
    }
    return encoded;
  }

  private decodeAudioChunks(chunks: string[], mimeType = 'audio/wav'): Blob {
    if (!chunks.length) {
      return new Blob([], { type: mimeType });
    }

    const byteArrays = chunks
      .filter((chunk) => !!chunk)
      .map((chunk) => {
        const binary = atob(chunk);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
          bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
      });

    return new Blob(byteArrays, { type: mimeType });
  }
}
