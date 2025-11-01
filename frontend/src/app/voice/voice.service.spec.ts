import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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

  it('should post audio data for transcription and emit recognized text', (done) => {
    const blob = new Blob(['audio'], { type: 'audio/webm' });

    service.recognizedText$.pipe(take(1)).subscribe((initial) => {
      expect(initial).toBe('');
    });

    service.transcribeAudio(blob).subscribe((text) => {
      expect(text).toBe('One Margherita');
      service.recognizedText$.pipe(take(1)).subscribe((value) => {
        expect(value).toBe('One Margherita');
        done();
      });
    });

    const req = httpMock.expectOne('/api/speech/stt');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({ text: 'One Margherita' });
  });

  it('should request TTS audio and expose synthesized blobs', (done) => {
    const blob = new Blob(['audio'], { type: 'audio/wav' });
    service.requestSynthesis('One Margherita').subscribe((response) => {
      expect(response).toEqual(blob);
      service.synthesizedAudio$.pipe(take(1)).subscribe((emitted) => {
        expect(emitted).toEqual(blob);
        done();
      });
    });

    const req = httpMock.expectOne('/api/speech/tts');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ text: 'One Margherita' });
    req.flush(blob);
  });

  it('should playback audio blobs via the Web Audio API', async () => {
    const blob = new Blob(['audio'], { type: 'audio/wav' });
    await service.playAudio(blob);
    expect(decodeSpy).toHaveBeenCalled();
    expect(createBufferSourceSpy).toHaveBeenCalled();
  });
});
