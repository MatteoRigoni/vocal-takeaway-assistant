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

  it('should post audio data for transcription, emit recognized text and store transcript entries', (done) => {
    const blob = new Blob(['audio'], { type: 'audio/webm' });

    service.recognizedText$.pipe(take(1)).subscribe((initial) => {
      expect(initial).toBe('');
    });

    service.transcribeAudio(blob).subscribe((text) => {
      expect(text).toBe('One Margherita');
      service.recognizedText$.pipe(take(1)).subscribe((value) => {
        expect(value).toBe('One Margherita');
        service.transcriptEntries$.pipe(take(1)).subscribe((entries) => {
          expect(entries).toEqual([{ speaker: 'user', text: 'One Margherita' }]);
          done();
        });
      });
    });

    const req = httpMock.expectOne('/api/voice/session');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      audioChunks: ['YXVkaW8='],
      responseText: null,
      voice: null,
      CallerId: 'TestUser',
    });
    req.flush({ recognizedText: 'One Margherita', responseAudioChunks: [] });
  });

  it('should request synthesized audio, expose decoded blobs and store assistant transcript entries', (done) => {
    service.requestSynthesis('One Margherita').subscribe((response) => {
      response.text().then((text) => {
        expect(text).toBe('audio');
        expect(response.type).toBe('audio/wav');

        service.synthesizedAudio$.pipe(take(1)).subscribe((emitted) => {
          emitted.text().then((emittedText) => {
            expect(emittedText).toBe('audio');
            expect(emitted.type).toBe('audio/wav');
            service.transcriptEntries$.pipe(take(1)).subscribe((entries) => {
              expect(entries).toEqual([{ speaker: 'assistant', text: 'One Margherita' }]);
              done();
            });
          });
        });
      });
    });

    const req = httpMock.expectOne('/api/voice/session');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      audioChunks: [],
      responseText: 'One Margherita',
      utteranceText: 'One Margherita',
      voice: null,
      CallerId: 'TestUser',
    });
    req.flush({ recognizedText: '', responseAudioChunks: ['YXVkaW8='] });
  });

  it('should playback audio blobs via the Web Audio API', async () => {
    const blob = new Blob(['audio'], { type: 'audio/wav' });
    await service.playAudio(blob);
    expect(decodeSpy).toHaveBeenCalled();
    expect(createBufferSourceSpy).toHaveBeenCalled();
  });
});
