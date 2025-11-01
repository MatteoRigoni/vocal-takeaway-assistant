import {
  AsyncPipe,
  NgClass,
  NgForOf,
  NgIf,
  NgSwitch,
  NgSwitchCase,
  NgSwitchDefault,
} from '@angular/common';
import { Component, OnDestroy, signal } from '@angular/core';
import { Subscription, firstValueFrom } from 'rxjs';
import { VoiceService } from './voice.service';

@Component({
  selector: 'app-voice-order',
  standalone: true,
  imports: [AsyncPipe, NgClass, NgForOf, NgIf, NgSwitch, NgSwitchCase, NgSwitchDefault],
  templateUrl: './voice-order.component.html',
  styleUrl: './voice-order.component.css',
})
export class VoiceOrderComponent implements OnDestroy {
  readonly status = signal<'idle' | 'recording' | 'submitting' | 'playing'>('idle');
  readonly recordingLevel = signal(0);
  readonly errorMessage = signal<string | null>(null);
  readonly confirmationMessage = signal<string | null>(null);
  get recognizedText() {
    return this.voiceService.recognizedText$;
  }
  get transcriptEntries() {
    return this.voiceService.transcriptEntries$;
  }

  private mediaRecorder?: MediaRecorder;
  private mediaStream?: MediaStream;
  private audioChunks: BlobPart[] = [];
  private audioContext?: AudioContext;
  private analyserNode?: AnalyserNode;
  private dataArray?: Uint8Array<ArrayBuffer>;
  private animationFrameId?: number;
  private streamSubscription?: Subscription;

  constructor(private readonly voiceService: VoiceService) {}

  ngOnDestroy(): void {
    this.stopRecording(true);
    this.streamSubscription?.unsubscribe();
    this.voiceService.disconnectStream();
  }

  async toggleRecording(): Promise<void> {
    if (this.status() === 'recording') {
      this.stopRecording();
      return;
    }

    await this.startRecording();
  }

  private async startRecording(): Promise<void> {
    this.errorMessage.set(null);

    if (!navigator.mediaDevices?.getUserMedia) {
      this.errorMessage.set('Microphone access is not supported in this browser.');
      return;
    }

    try {
      this.mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
      this.status.set('recording');
      this.setupAnalyser(this.mediaStream);
      this.setupRecorder(this.mediaStream);
      this.streamSubscription?.unsubscribe();
      this.streamSubscription = this.voiceService.connectToVoiceStream().subscribe();
    } catch (error) {
      this.errorMessage.set('Microphone permission was denied.');
      this.status.set('idle');
    }
  }

  private setupAnalyser(stream: MediaStream): void {
    this.audioContext = new AudioContext();
    const source = this.audioContext.createMediaStreamSource(stream);
    this.analyserNode = this.audioContext.createAnalyser();
    this.analyserNode.fftSize = 256;
    source.connect(this.analyserNode);
    const bufferLength = this.analyserNode.frequencyBinCount;
    this.dataArray = new Uint8Array(new ArrayBuffer(bufferLength));
    this.animateLevel();
  }

  private setupRecorder(stream: MediaStream): void {
    this.audioChunks = [];
    try {
      this.mediaRecorder = new MediaRecorder(stream);
    } catch (error) {
      this.errorMessage.set('Recording is not supported in this environment.');
      this.status.set('idle');
      return;
    }

    this.mediaRecorder.ondataavailable = (event) => {
      if (event.data?.size) {
        this.audioChunks.push(event.data);
      }
    };

    this.mediaRecorder.onstop = () => {
      const audioBlob = new Blob(this.audioChunks, { type: this.mediaRecorder?.mimeType ?? 'audio/webm' });
      this.submitAudio(audioBlob);
    };

    this.mediaRecorder.start();
  }

  private animateLevel(): void {
    if (!this.analyserNode || !this.dataArray) {
      return;
    }

    const draw = () => {
      if (!this.analyserNode || !this.dataArray) {
        return;
      }
      this.analyserNode.getByteTimeDomainData(
        this.dataArray as unknown as Uint8Array<ArrayBuffer>
      );
      const sum = this.dataArray.reduce((acc, value) => acc + Math.abs(value - 128), 0);
      const average = sum / this.dataArray.length;
      this.recordingLevel.set(Math.min(1, average / 64));
      if (this.status() === 'recording') {
        this.animationFrameId = requestAnimationFrame(draw);
      }
    };

    this.animationFrameId = requestAnimationFrame(draw);
  }

  stopRecording(force = false): void {
    if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
      this.mediaRecorder.stop();
    } else if (force) {
      this.submitAudio(new Blob());
    }

    this.stopStream();
    this.streamSubscription?.unsubscribe();
    this.streamSubscription = undefined;
    this.voiceService.disconnectStream();
    this.status.set('idle');
  }

  private stopStream(): void {
    this.animationFrameId && cancelAnimationFrame(this.animationFrameId);
    this.animationFrameId = undefined;

    this.mediaStream?.getTracks().forEach((track) => track.stop());
    this.mediaStream = undefined;

    if (this.audioContext) {
      this.audioContext.close();
      this.audioContext = undefined;
    }
  }

  private async submitAudio(blob: Blob): Promise<void> {
    if (!blob.size) {
      return;
    }

    this.status.set('submitting');
    try {
      const session = await firstValueFrom(this.voiceService.transcribeAudio(blob));
      const transcript = session?.recognizedText?.trim() ?? '';
      if (transcript) {
        this.confirmationMessage.set(`Heard: ${transcript}`);
      }

      const prompt = session?.promptText?.trim() ?? '';
      if (prompt) {
        this.confirmationMessage.set(prompt);
      }

      const audioBlob = this.voiceService.createAudioBlob(session?.responseAudioChunks ?? []);
      if (audioBlob.size > 0) {
        this.status.set('playing');
        await this.voiceService.playAudio(audioBlob);
      }

      if (!prompt) {
        this.confirmationMessage.set('Ready to confirm your order?');
      }
    } catch (error) {
      this.errorMessage.set('We could not reach the speech service. Please try again.');
    } finally {
      this.status.set('idle');
      this.stopStream();
    }
  }
}
