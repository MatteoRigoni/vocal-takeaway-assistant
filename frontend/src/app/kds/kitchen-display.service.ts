import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { firstValueFrom, Subject } from 'rxjs';

import {
  KdsOrderTicketDto,
  KitchenTicketStatus,
  KitchenTicketViewModel,
  TicketFilter,
} from './kds-ticket.model';

@Injectable({ providedIn: 'root' })
export class KitchenDisplayService {
  private readonly http = inject(HttpClient);

  private readonly ticketsSignal = signal<KitchenTicketViewModel[]>([]);
  private readonly filterSignal = signal<TicketFilter>('all');
  private readonly lastSyncSignal = signal<Date | null>(null);
  private readonly nextRefreshSignal = signal<Date | null>(null);
  private readonly connectionSignal = signal<'connecting' | 'connected' | 'disconnected'>('disconnected');
  private readonly errorSignal = signal<string | null>(null);
  private readonly updatingTickets = signal(new Set<number>());

  private readonly readySubject = new Subject<KitchenTicketViewModel>();

  private connection: HubConnection | null = null;
  private refreshTimer?: ReturnType<typeof setInterval>;
  private readonly refreshIntervalMs = 30000;
  private isActive = false;
  private readonly ticketMap = new Map<number, KitchenTicketViewModel>();

  private readonly apiBase = this.resolveApiBaseUrl();

  readonly tickets: Signal<KitchenTicketViewModel[]> = computed(() => {
    const filter = this.filterSignal();
    const tickets = this.ticketsSignal();
    if (filter === 'all') {
      return tickets;
    }

    return tickets.filter((ticket) => this.matchesFilter(ticket.status, filter));
  });

  readonly allTickets: Signal<KitchenTicketViewModel[]> = computed(() => this.ticketsSignal());
  readonly currentFilter: Signal<TicketFilter> = computed(() => this.filterSignal());
  readonly lastSync: Signal<Date | null> = computed(() => this.lastSyncSignal());
  readonly nextRefresh: Signal<Date | null> = computed(() => this.nextRefreshSignal());
  readonly connectionStatus: Signal<'connecting' | 'connected' | 'disconnected'> = computed(
    () => this.connectionSignal(),
  );
  readonly error: Signal<string | null> = computed(() => this.errorSignal());
  readonly ready$ = this.readySubject.asObservable();

  async start(): Promise<void> {
    if (this.isActive) {
      return;
    }

    this.isActive = true;
    await this.refreshTickets();
    this.scheduleAutoRefresh();
    await this.ensureConnection();
  }

  stop(): void {
    this.isActive = false;
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = undefined;
    }

    if (this.connection) {
      void this.connection.stop();
      this.connection = null;
      this.connectionSignal.set('disconnected');
    }
  }

  async refreshTickets(): Promise<void> {
    try {
      const url = this.createUrl('/orders/kds');
      const payload = await firstValueFrom(this.http.get<KdsOrderTicketDto[]>(url));
      
      // Validazione aggiuntiva prima di passare a ingestSnapshot
      if (!payload) {
        console.warn('refreshTickets: payload is null or undefined');
        this.ingestSnapshot([]); // Passa array vuoto invece di null/undefined
        return;
      }
      
      this.ingestSnapshot(payload);
      const now = new Date();
      this.lastSyncSignal.set(now);
      this.nextRefreshSignal.set(new Date(now.getTime() + this.refreshIntervalMs));
      this.errorSignal.set(null);
    } catch (error) {
      console.error('Unable to refresh kitchen tickets', error);
      const errorMessage = error instanceof Error ? error.message : 'Unknown error';
      this.errorSignal.set(`Unable to refresh tickets: ${errorMessage}. Retrying automatically.`);
      
      // In caso di errore, passa array vuoto per evitare crash
      this.ingestSnapshot([]);
    }
  }

  ingestSnapshot(tickets: KdsOrderTicketDto[]): void {
    // Validazione: assicurati che tickets sia un array iterabile
    if (!Array.isArray(tickets)) {
      console.error('ingestSnapshot: tickets is not an array', tickets);
      this.errorSignal.set('Invalid response format from server');
      return;
    }
    
    this.ticketMap.clear();
    for (const ticket of tickets) {
      const viewModel = this.toViewModel(ticket);
      this.ticketMap.set(viewModel.orderId, viewModel);
    }
    this.publishTickets();
  }

  async updateTicketStatus(orderId: number, status: KitchenTicketStatus): Promise<boolean> {
    this.setTicketUpdating(orderId, true);

    try {
      const url = this.createUrl(`/orders/${orderId}`);
      await firstValueFrom(this.http.patch(url, { status }));
      return true;
    } catch (error) {
      console.error(`Unable to update order ${orderId} to status ${status}`, error);
      const message = error instanceof Error ? error.message : 'Unknown error';
      this.errorSignal.set(`Failed to update order ${orderId}: ${message}`);
      return false;
    } finally {
      this.setTicketUpdating(orderId, false);
    }
  }

  setFilter(filter: TicketFilter): void {
    this.filterSignal.set(filter);
  }

  isTicketVisible(ticket: KitchenTicketViewModel, filter: TicketFilter): boolean {
    return this.matchesFilter(ticket.status, filter);
  }

  isTicketUpdating(orderId: number): boolean {
    return this.updatingTickets().has(orderId);
  }

  private scheduleAutoRefresh(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
    }

    this.refreshTimer = setInterval(() => {
      if (!this.isActive) {
        return;
      }

      void this.refreshTickets();
    }, this.refreshIntervalMs);
  }

  private async ensureConnection(): Promise<void> {
    if (!this.isActive) {
      return;
    }

    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }

    if (this.connection && this.connection.state === HubConnectionState.Disconnected) {
      await this.startHubConnection(this.connection);
      return;
    }

    const builder = new HubConnectionBuilder()
      .withUrl(this.createUrl('/hubs/kds'))
      .withAutomaticReconnect([0, 2000, 5000, 10000]);

    this.connection = builder.build();
    this.registerHubHandlers(this.connection);

    await this.startHubConnection(this.connection);
  }

  private async startHubConnection(connection: HubConnection): Promise<void> {
    this.connectionSignal.set('connecting');
    try {
      await connection.start();
      this.connectionSignal.set('connected');
      this.errorSignal.set(null);
    } catch (error) {
      console.error('Unable to connect to kitchen hub', error);
      this.connectionSignal.set('disconnected');
      this.errorSignal.set('Live updates are temporarily unavailable.');
      if (this.isActive) {
        setTimeout(() => {
          void this.ensureConnection();
        }, 5000);
      }
    }
  }

  private registerHubHandlers(connection: HubConnection): void {
    connection.on('TicketCreated', (ticket: KdsOrderTicketDto) => {
      this.upsertTicket(this.toViewModel(ticket));
    });

    connection.on('TicketUpdated', (ticket: KdsOrderTicketDto) => {
      this.upsertTicket(this.toViewModel(ticket));
    });

    connection.on('TicketRemoved', (payload: { orderId?: number; OrderId?: number }) => {
      const id = payload.orderId ?? payload.OrderId;
      if (typeof id === 'number') {
        this.removeTicket(id);
      }
    });

    connection.onreconnected(() => {
      this.connectionSignal.set('connected');
      void this.refreshTickets();
    });

    connection.onreconnecting(() => {
      this.connectionSignal.set('connecting');
    });

    connection.onclose(() => {
      this.connectionSignal.set('disconnected');
      if (this.isActive) {
        setTimeout(() => {
          void this.ensureConnection();
        }, 2000);
      }
    });
  }

  private upsertTicket(ticket: KitchenTicketViewModel): void {
    const previous = this.ticketMap.get(ticket.orderId);
    this.ticketMap.set(ticket.orderId, ticket);
    this.publishTickets();

    if (ticket.status === 'Ready' && previous?.status !== 'Ready') {
      this.readySubject.next(ticket);
    }
  }

  private removeTicket(orderId: number): void {
    if (this.ticketMap.delete(orderId)) {
      this.publishTickets();
    }
  }

  private publishTickets(): void {
    const ordered = Array.from(this.ticketMap.values()).sort(
      (a, b) => a.createdAtUtc.getTime() - b.createdAtUtc.getTime(),
    );
    this.ticketsSignal.set(ordered);
  }

  private toViewModel(ticket: KdsOrderTicketDto): KitchenTicketViewModel {
    return {
      orderId: ticket.orderId,
      orderCode: ticket.orderCode,
      status: ticket.status,
      createdAtUtc: this.parseUtcDate(ticket.createdAtUtc),
      pickupAtUtc: this.parseUtcDate(ticket.pickupAtUtc),
      customerName: ticket.customerName,
      customerPhone: ticket.customerPhone,
      notes: ticket.notes,
      totalAmount: ticket.totalAmount,
      items: ticket.items.map((item) => ({
        id: item.id,
        productName: item.productName,
        variantName: item.variantName,
        modifiers: Array.isArray(item.modifiers) ? item.modifiers : [],
        quantity: item.quantity,
      })),
    };
  }

  private matchesFilter(status: KitchenTicketStatus, filter: TicketFilter): boolean {
    switch (filter) {
      case 'pending':
        return status === 'Received';
      case 'prepping':
        return status === 'InPreparation';
      case 'ready':
        return status === 'Ready';
      default:
        return true;
    }
  }

  private setTicketUpdating(orderId: number, updating: boolean): void {
    this.updatingTickets.update((current) => {
      const next = new Set(current);
      if (updating) {
        next.add(orderId);
      } else {
        next.delete(orderId);
      }
      return next;
    });
  }

  private resolveApiBaseUrl(): string {
    const globalConfig = (globalThis as { __TAKEAWAY_API__?: { baseUrl?: string } }).__TAKEAWAY_API__;
    const metaBase = (import.meta as { env?: { NG_APP_API_URL?: string } }).env?.NG_APP_API_URL;
    const base = globalConfig?.baseUrl ?? metaBase ?? '';
    return base.endsWith('/') ? base.slice(0, -1) : base;
  }

  private createUrl(path: string): string {
    const normalized = path.startsWith('/') ? path : `/${path}`;
    if (!this.apiBase) {
      return normalized;
    }

    return `${this.apiBase}${normalized}`;
  }

  private parseUtcDate(value: string): Date {
    const trimmed = value?.trim();
    if (!trimmed) {
      return new Date(NaN);
    }

    const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(trimmed);
    const candidate = hasZone ? trimmed : `${trimmed}Z`;
    const parsed = new Date(candidate);

    if (!Number.isNaN(parsed.getTime())) {
      return parsed;
    }

    return new Date(trimmed);
  }
}
