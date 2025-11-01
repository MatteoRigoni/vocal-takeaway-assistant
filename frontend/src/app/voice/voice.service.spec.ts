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

  it('should post audio data, update slots, and emit audio blobs', (done) => {
    const blob = new Blob(['audio'], { type: 'audio/webm' });

    service.recognizedText$.pipe(take(1)).subscribe((initial) => {
      expect(initial).toBe('');
    });

    service.processAudio(blob).subscribe(({ response, audio }) => {
      expect(response.recognizedText).toBe('One Margherita');
      expect(response.promptText).toBe('Any toppings?');
      expect(audio.type).toBe('audio/wav');

      service.recognizedText$.pipe(take(1)).subscribe((value) => {
        expect(value).toBe('One Margherita');
      });

      service.slots$.pipe(take(1)).subscribe((slots) => {
        expect(slots?.product.name).toBe('Margherita');
        expect(slots?.quantity.quantity).toBe(2);
        done();
      });
    });

    const req = httpMock.expectOne('/api/voice/session');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.audioChunks).toEqual(['YXVkaW8=']);
    expect(req.request.body.utteranceText).toBeNull();
    expect(req.request.body.voice).toBeNull();
    expect(req.request.body.callerId).toMatch(/^web-/);

    req.flush({
      recognizedText: 'One Margherita',
      responseAudioChunks: ['YXVkaW8='],
      promptText: 'Any toppings?',
      dialogState: 'Ordering',
      isSessionComplete: false,
      slots: {
        product: { productId: 1, name: 'Margherita', isFilled: true },
        variant: { variantId: null, name: null, productId: null, isFilled: false },
        quantity: { quantity: 2, isFilled: true },
        modifiers: { selections: [], isFilled: false, isExplicitNone: false },
        pickupTime: { value: null, isFilled: false },
      },
      metadata: {},
    });
  });

  it('should playback audio blobs via the Web Audio API', async () => {
    const blob = new Blob(['audio'], { type: 'audio/wav' });
    await service.playAudio(blob);
    expect(decodeSpy).toHaveBeenCalled();
    expect(createBufferSourceSpy).toHaveBeenCalled();
  });
});
