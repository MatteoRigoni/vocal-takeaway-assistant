import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, ReplaySubject, from, map, switchMap, tap } from 'rxjs';

interface ProductSlotDto {
  productId: number | null;
  name: string | null;
  isFilled: boolean;
}

interface VariantSlotDto {
  variantId: number | null;
  name: string | null;
  productId: number | null;
  isFilled: boolean;
}

interface QuantitySlotDto {
  quantity: number | null;
  isFilled: boolean;
}

interface ModifierSelectionDto {
  modifierId: number;
  name: string;
  productId: number;
}

interface ModifiersSlotDto {
  selections: ModifierSelectionDto[];
  isFilled: boolean;
  isExplicitNone: boolean;
}

interface PickupTimeSlotDto {
  value: string | null;
  isFilled: boolean;
}

export interface VoiceOrderSlotsDto {
  product: ProductSlotDto;
  variant: VariantSlotDto;
  quantity: QuantitySlotDto;
  modifiers: ModifiersSlotDto;
  pickupTime: PickupTimeSlotDto;
}

export interface VoiceSessionResponse {
  recognizedText: string;
  responseAudioChunks: string[];
  promptText: string;
  dialogState: string;
  isSessionComplete: boolean;
  slots: VoiceOrderSlotsDto;
  metadata?: Record<string, string>;
}

export interface ProcessedVoiceResponse {
  response: VoiceSessionResponse;
  audio: Blob;
}

@Injectable({ providedIn: 'root' })
export class VoiceService {
  private readonly voiceSessionEndpoint = '/api/voice/session';
  private readonly callerId = this.generateCallerId();
  private currentSlots: VoiceOrderSlotsDto | null = null;

  private readonly recognizedTextSubject = new BehaviorSubject<string>('');
  readonly recognizedText$ = this.recognizedTextSubject.asObservable();

  private readonly promptSubject = new BehaviorSubject<string>('');
  readonly prompt$ = this.promptSubject.asObservable();

  private readonly slotsSubject = new BehaviorSubject<VoiceOrderSlotsDto | null>(null);
  readonly slots$ = this.slotsSubject.asObservable();

  private readonly synthesizedAudioSubject = new ReplaySubject<Blob>(1);
  readonly synthesizedAudio$ = this.synthesizedAudioSubject.asObservable();

  private audioContext?: AudioContext;

  constructor(private readonly http: HttpClient) {}

  processAudio(audioBlob: Blob): Observable<ProcessedVoiceResponse> {
    return from(this.encodeAudioBlob(audioBlob)).pipe(
      switchMap((audioChunks) =>
        this.http.post<VoiceSessionResponse>(this.voiceSessionEndpoint, {
          callerId: this.callerId,
          audioChunks,
          utteranceText: null,
          slots: this.currentSlots,
          voice: null,
        })
      ),
      map((response) => ({ response, audio: this.decodeAudioChunks(response?.responseAudioChunks ?? []) })),
      tap(({ response, audio }) => {
        this.currentSlots = response?.slots ?? null;
        this.recognizedTextSubject.next(response?.recognizedText ?? '');
        this.promptSubject.next(response?.promptText ?? '');
        this.slotsSubject.next(this.currentSlots);
        if (audio.size > 0) {
          this.synthesizedAudioSubject.next(audio);
        }
        if (response?.isSessionComplete) {
          this.resetSession();
        }
      })
    );
  }

  resetSession(): void {
    this.currentSlots = null;
    this.slotsSubject.next(null);
  }

  async playAudio(blob: Blob): Promise<void> {
    if (!blob.size) {
      return;
    }
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
    return this.recognizedText$;
  }

  disconnectStream(): void {
    // No streaming hub is currently available; method retained for API compatibility.
  }

  private generateCallerId(): string {
    const globalCrypto = (globalThis as unknown as { crypto?: Crypto & { randomUUID?: () => string } }).crypto;
    if (globalCrypto?.randomUUID) {
      return `web-${globalCrypto.randomUUID()}`;
    }
    return `web-${Date.now().toString(36)}`;
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
