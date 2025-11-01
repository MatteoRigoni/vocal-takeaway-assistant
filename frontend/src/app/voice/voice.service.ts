import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, ReplaySubject, from, map, switchMap, tap } from 'rxjs';

interface VoiceSessionResponse {
  recognizedText: string;
  responseAudioChunks: string[];
}

@Injectable({ providedIn: 'root' })
export class VoiceService {
  private readonly voiceSessionEndpoint = '/api/voice/session';

  private readonly recognizedTextSubject = new BehaviorSubject<string>('');
  readonly recognizedText$ = this.recognizedTextSubject.asObservable();

  private readonly synthesizedAudioSubject = new ReplaySubject<Blob>(1);
  readonly synthesizedAudio$ = this.synthesizedAudioSubject.asObservable();

  private audioContext?: AudioContext;

  constructor(private readonly http: HttpClient) {}

  transcribeAudio(audioBlob: Blob): Observable<string> {
    return from(this.encodeAudioBlob(audioBlob)).pipe(
      switchMap((audioChunks) =>
        this.http.post<VoiceSessionResponse>(this.voiceSessionEndpoint, {
          audioChunks,
          responseText: null,
          voice: null,
        })
      ),
      map((response) => response?.recognizedText ?? ''),
      tap((text) => this.recognizedTextSubject.next(text))
    );
  }

  requestSynthesis(text: string): Observable<Blob> {
    console.log('[VoiceService] Richiesta sintesi per il testo:', text);
    return this.http
      .post<VoiceSessionResponse>(this.voiceSessionEndpoint, {
        audioChunks: [],
        responseText: text,
        voice: null,
      })
      .pipe(
        tap((response) => {
          console.log('[VoiceService] Risposta sessione voce:', response);
          if (response && response.responseAudioChunks) {
            response.responseAudioChunks.forEach((chunk, idx) => {
              const anteprima = chunk ? chunk.substring(0, 60) + '...' : '(vuoto/null)';
              let decoded = '';
              try {
                decoded = atob(chunk);
              } catch { decoded = '(errore decodifica base64)'; }
              console.log(`[VoiceService] Chunk audio[${idx}] base64:`, anteprima);
              // Provare a stampare come testo umano  
              console.log(`[VoiceService] Chunk audio[${idx}] decodificato:`, decoded.substring(0, 120));
            });
          }
        }),
        map((response) => this.decodeAudioChunks(response?.responseAudioChunks ?? [])),
        tap((blob) => {
          console.log('[VoiceService] Blob audio sintetizzato:', blob);
          this.synthesizedAudioSubject.next(blob);
        })
      );
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
