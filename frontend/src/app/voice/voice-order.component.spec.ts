import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { of } from 'rxjs';
import { VoiceOrderComponent } from './voice-order.component';
import { VoiceService } from './voice.service';

class MockMediaRecorder {
  state: 'inactive' | 'recording' | 'paused' = 'inactive';
  ondataavailable: ((event: BlobEvent) => void) | null = null;
  onstop: (() => void) | null = null;

  constructor(public readonly stream: MediaStream) {}

  start(): void {
    this.state = 'recording';
    if (this.ondataavailable) {
      const blob = new Blob(['audio'], { type: 'audio/webm' });
      this.ondataavailable({ data: blob } as BlobEvent);
    }
  }

  stop(): void {
    this.state = 'inactive';
    this.onstop?.();
  }
}

describe('VoiceOrderComponent', () => {
  let fixture: ComponentFixture<VoiceOrderComponent>;
  let component: VoiceOrderComponent;
  let voiceService: jasmine.SpyObj<VoiceService>;
  let originalMediaRecorder: typeof MediaRecorder | undefined;
  let originalMediaDevices: MediaDevices | undefined;

  beforeEach(async () => {
    voiceService = jasmine.createSpyObj<VoiceService>('VoiceService', [
      'transcribeAudio',
      'playAudio',
      'connectToVoiceStream',
      'disconnectStream',
      'createAudioBlob',
    ], {
      recognizedText$: of('One Margherita'),
      transcriptEntries$: of([]),
    });

    voiceService.transcribeAudio.and.returnValue(
      of({
        recognizedText: 'One Margherita',
        responseAudioChunks: ['YXVkaW8='],
        promptText: 'Please confirm your order.',
        dialogState: 'Confirming',
        isSessionComplete: false,
        metadata: null,
      })
    );
    voiceService.createAudioBlob.and.returnValue(new Blob(['audio'], { type: 'audio/wav' }));
    voiceService.playAudio.and.returnValue(Promise.resolve());
    voiceService.connectToVoiceStream.and.returnValue(of('One Margherita'));

    originalMediaRecorder = (window as any).MediaRecorder;
    (window as any).MediaRecorder = MockMediaRecorder as unknown as typeof MediaRecorder;

    originalMediaDevices = navigator.mediaDevices;
    Object.defineProperty(navigator, 'mediaDevices', {
      value: {
        getUserMedia: jasmine
          .createSpy('getUserMedia')
          .and.resolveTo({
            getTracks: () => [{ stop: jasmine.createSpy('stop') }],
          } as unknown as MediaStream),
      },
      configurable: true,
    });

    await TestBed.configureTestingModule({
      imports: [VoiceOrderComponent],
      providers: [{ provide: VoiceService, useValue: voiceService }],
    }).compileComponents();

    fixture = TestBed.createComponent(VoiceOrderComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    if (originalMediaRecorder) {
      (window as any).MediaRecorder = originalMediaRecorder;
    } else {
      delete (window as any).MediaRecorder;
    }
    if (originalMediaDevices) {
      Object.defineProperty(navigator, 'mediaDevices', { value: originalMediaDevices, configurable: true });
    } else {
      delete (navigator as any).mediaDevices;
    }
  });

  it('should request microphone access and submit audio when recording stops', fakeAsync(() => {
    const getUserMediaSpy = navigator.mediaDevices.getUserMedia as jasmine.Spy;

    component.toggleRecording();
    flushMicrotasks();

    expect(getUserMediaSpy).toHaveBeenCalled();
    expect(voiceService.connectToVoiceStream).toHaveBeenCalled();

    component.stopRecording();
    flushMicrotasks();

    expect(voiceService.transcribeAudio).toHaveBeenCalled();
    expect(voiceService.createAudioBlob).toHaveBeenCalledWith(['YXVkaW8=']);
    expect(voiceService.playAudio).toHaveBeenCalled();
  }));

  it('should surface microphone permission errors', fakeAsync(() => {
    const rejection = Promise.reject(new Error('denied'));
    (navigator.mediaDevices.getUserMedia as jasmine.Spy).and.returnValue(rejection);

    component.toggleRecording();
    flushMicrotasks();

    expect(component.errorMessage()).toContain('Microphone permission');
    expect(component.status()).toBe('idle');
  }));
});
