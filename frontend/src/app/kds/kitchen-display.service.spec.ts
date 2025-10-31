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

  it('updates ticket status via the API and refreshes the local cache', async () => {
    const createdAt = new Date();
    const pickupAt = new Date(Date.now() + 15 * 60 * 1000);
    service.ingestSnapshot([
      {
        orderId: 7,
        orderCode: 'C007',
        status: 'Received',
        createdAtUtc: createdAt.toISOString(),
        pickupAtUtc: pickupAt.toISOString(),
        customerName: null,
        customerPhone: null,
        notes: null,
        totalAmount: 12,
        items: [],
      },
    ]);

    const updatePromise = service.updateTicketStatus(7, 'InPreparation');
    const req = httpMock.expectOne('/orders/7');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'InPreparation' });
    req.flush({});

    await updatePromise;

    const ticket = service.allTickets()[0];
    expect(ticket.status).toBe('InPreparation');
  });

  it('removes completed tickets after marking them picked up', async () => {
    const createdAt = new Date();
    const pickupAt = new Date(Date.now() + 5 * 60 * 1000);
    service.ingestSnapshot([
      {
        orderId: 9,
        orderCode: 'C009',
        status: 'Ready',
        createdAtUtc: createdAt.toISOString(),
        pickupAtUtc: pickupAt.toISOString(),
        customerName: null,
        customerPhone: null,
        notes: null,
        totalAmount: 18,
        items: [],
      },
    ]);

    const updatePromise = service.updateTicketStatus(9, 'Completed');
    const req = httpMock.expectOne('/orders/9');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Completed' });
    req.flush({});

    await updatePromise;

    expect(service.allTickets().length).toBe(0);
  });
});
