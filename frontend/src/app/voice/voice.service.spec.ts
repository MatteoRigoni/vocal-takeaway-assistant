import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { take } from 'rxjs/operators';
import { VoiceService } from './voice.service';

describe('VoiceService', () => {
  let service: VoiceService;
  let httpMock: HttpTestingController;
  let originalAudioContext: typeof AudioContext | undefined;
  let decodeSpy: jasmine.Spy;
  let createBufferSourceSpy: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });

    service = TestBed.inject(VoiceService);
    httpMock = TestBed.inject(HttpTestingController);

    originalAudioContext = (window as any).AudioContext;
    decodeSpy = jasmine.createSpy('decodeAudioData').and.callFake((buffer: ArrayBuffer) =>
      Promise.resolve(buffer as unknown as AudioBuffer)
    );
    createBufferSourceSpy = jasmine.createSpy('createBufferSource').and.callFake(() => {
      const node: any = {
        connect: jasmine.createSpy('connect'),
        start: jasmine.createSpy('start').and.callFake(() => {
          node.onended?.();
        }),
        onended: undefined,
      };
      return node;
    });

    class StubAudioContext {
      decodeAudioData = decodeSpy;
      createBufferSource = createBufferSourceSpy;
      destination = {};
      close = jasmine.createSpy('close');
    }

    (window as any).AudioContext = StubAudioContext as unknown as typeof AudioContext;
  });

  afterEach(() => {
    httpMock.verify();
    if (originalAudioContext) {
      (window as any).AudioContext = originalAudioContext;
    } else {
      delete (window as any).AudioContext;
    }
  });

  it('should post audio data for transcription and update voice session state subjects', (done) => {
    const blob = new Blob(['audio'], { type: 'audio/webm' });

    service.transcribeAudio(blob).subscribe((response) => {
      expect(response).toEqual({
        recognizedText: 'One Margherita',
        responseAudioChunks: [],
        promptText: 'Please confirm your order.',
        dialogState: 'Confirming',
        isSessionComplete: false,
        metadata: { intent: 'order' },
      });

      service.recognizedText$.pipe(take(1)).subscribe((value) => {
        expect(value).toBe('One Margherita');
        service.promptText$.pipe(take(1)).subscribe((prompt) => {
          expect(prompt).toBe('Please confirm your order.');
          service.dialogState$.pipe(take(1)).subscribe((state) => {
            expect(state).toBe('Confirming');
            service.sessionComplete$.pipe(take(1)).subscribe((complete) => {
              expect(complete).toBeFalse();
              service.metadata$.pipe(take(1)).subscribe((metadata) => {
                expect(metadata).toEqual({ intent: 'order' });
                service.transcriptEntries$.pipe(take(1)).subscribe((entries) => {
                  expect(entries).toEqual([
                    { speaker: 'user', text: 'One Margherita' },
                    { speaker: 'assistant', text: 'Please confirm your order.' },
                  ]);
                  done();
                });
              });
            });
          });
        });
      });
    });

    const req = httpMock.expectOne('/api/voice/session');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      audioChunks: ['YXVkaW8='],
      utteranceText: null,
      voice: null,
      CallerId: 'TestUser',
    });
    req.flush({
      recognizedText: 'One Margherita',
      responseAudioChunks: [],
      promptText: 'Please confirm your order.',
      dialogState: 'Confirming',
      isSessionComplete: false,
      metadata: { intent: 'order' },
    });
  });

  it('should decode synthesized audio chunks and emit blobs for playback', async () => {
    const blob = new Blob(['audio'], { type: 'audio/webm' });

    const audioPromise = firstValueFrom(service.synthesizedAudio$.pipe(take(1)));

    service.transcribeAudio(blob).subscribe();

    const req = httpMock.expectOne('/api/voice/session');
    expect(req.request.method).toBe('POST');
    req.flush({
      recognizedText: 'Grazie',
      responseAudioChunks: ['YXVkaW8='],
      promptText: '',
      dialogState: 'Ordering',
      isSessionComplete: false,
      metadata: null,
    });

    const emitted = await audioPromise;
    const text = await emitted.text();
    expect(text).toBe('audio');
    expect(emitted.type).toBe('audio/wav');
  });

  it('should playback audio blobs via the Web Audio API', async () => {
    const blob = new Blob(['audio'], { type: 'audio/wav' });
    await service.playAudio(blob);
    expect(decodeSpy).toHaveBeenCalled();
    expect(createBufferSourceSpy).toHaveBeenCalled();
  });
});
