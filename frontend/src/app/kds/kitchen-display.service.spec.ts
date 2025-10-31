import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';

import { KitchenDisplayService } from './kitchen-display.service';
import { KdsOrderTicketDto } from './kds-ticket.model';

describe('KitchenDisplayService', () => {
  let service: KitchenDisplayService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [KitchenDisplayService],
    });

    service = TestBed.inject(KitchenDisplayService);
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
});
