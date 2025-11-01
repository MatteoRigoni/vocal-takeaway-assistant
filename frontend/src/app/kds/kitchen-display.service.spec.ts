import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { KitchenDisplayService } from './kitchen-display.service';
import { KdsOrderTicketDto } from './kds-ticket.model';

describe('KitchenDisplayService', () => {
  let service: KitchenDisplayService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [KitchenDisplayService],
    });

    service = TestBed.inject(KitchenDisplayService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('hydrates a snapshot of tickets', () => {
    const dto: KdsOrderTicketDto = {
      orderId: 42,
      orderCode: 'A042',
      status: 'Received',
      createdAtUtc: new Date().toISOString(),
      pickupAtUtc: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
      customerName: 'Test',
      customerPhone: null,
      notes: 'No onions',
      totalAmount: 19.5,
      items: [
        {
          id: 1,
          productName: 'Margherita',
          variantName: null,
          modifiers: ['Extra cheese'],
          quantity: 1,
        },
      ],
    };

    service.ingestSnapshot([dto]);
    expect(service.allTickets().length).toBe(1);
    expect(service.tickets().length).toBe(1);
  });

  it('normalizes naive timestamps as UTC', () => {
    const naiveTimestamp = '2025-05-13T21:15:00';
    const dto: KdsOrderTicketDto = {
      orderId: 77,
      orderCode: 'N077',
      status: 'Received',
      createdAtUtc: naiveTimestamp,
      pickupAtUtc: naiveTimestamp,
      customerName: null,
      customerPhone: null,
      notes: null,
      totalAmount: 12,
      items: [],
    };

    service.ingestSnapshot([dto]);
    const ticket = service.allTickets()[0];
    expect(ticket.createdAtUtc.toISOString()).toBe(new Date(`${naiveTimestamp}Z`).toISOString());
    expect(ticket.pickupAtUtc.toISOString()).toBe(new Date(`${naiveTimestamp}Z`).toISOString());
  });

  it('applies filters to the active view', () => {
    const dto: KdsOrderTicketDto = {
      orderId: 1,
      orderCode: 'B001',
      status: 'Received',
      createdAtUtc: new Date().toISOString(),
      pickupAtUtc: new Date().toISOString(),
      customerName: null,
      customerPhone: null,
      notes: null,
      totalAmount: 10,
      items: [],
    };

    service.ingestSnapshot([dto]);
    service.setFilter('ready');
    expect(service.tickets().length).toBe(0);

    service.setFilter('pending');
    expect(service.tickets().length).toBe(1);
  });

  it('sends status updates via PATCH and toggles busy state', async () => {
    const dto: KdsOrderTicketDto = {
      orderId: 7,
      orderCode: 'T007',
      status: 'Received',
      createdAtUtc: new Date().toISOString(),
      pickupAtUtc: new Date().toISOString(),
      customerName: null,
      customerPhone: null,
      notes: null,
      totalAmount: 10,
      items: [],
    };

    service.ingestSnapshot([dto]);

    const promise = service.updateTicketStatus(7, 'InPreparation');
    const req = httpMock.expectOne('/orders/7');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'InPreparation' });
    expect(service.isTicketUpdating(7)).toBeTrue();

    req.flush({});
    const success = await promise;
    expect(success).toBeTrue();
    expect(service.isTicketUpdating(7)).toBeFalse();
  });

  it('reports failures when update requests error', async () => {
    const dto: KdsOrderTicketDto = {
      orderId: 9,
      orderCode: 'T009',
      status: 'Ready',
      createdAtUtc: new Date().toISOString(),
      pickupAtUtc: new Date().toISOString(),
      customerName: null,
      customerPhone: null,
      notes: null,
      totalAmount: 10,
      items: [],
    };

    service.ingestSnapshot([dto]);

    const promise = service.updateTicketStatus(9, 'Completed');
    const req = httpMock.expectOne('/orders/9');
    req.flush('boom', { status: 500, statusText: 'Server Error' });

    const success = await promise;
    expect(success).toBeFalse();
    expect(service.isTicketUpdating(9)).toBeFalse();
    expect(service.error()).toContain('Failed to update order 9');
  });
});
