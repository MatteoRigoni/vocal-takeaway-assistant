import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, Signal, computed, inject, signal } from '@angular/core';

import { KitchenDisplayService } from './kitchen-display.service';
import { KitchenTicketStatus, KitchenTicketViewModel, TicketFilter } from './kds-ticket.model';

type ConnectionStatus = 'connecting' | 'connected' | 'disconnected';

type FilterDefinition = { key: TicketFilter; label: string; description: string };
type TicketActionVariant = 'primary' | 'secondary' | 'ghost';
type TicketAction = {
  status: KitchenTicketStatus;
  label: string;
  variant: TicketActionVariant;
  confirm?: string;
};

@Component({
  selector: 'app-kds-board',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './kds-board.component.html',
  styleUrl: './kds-board.component.css',
})
export class KdsBoardComponent implements OnInit {
  private readonly kitchenDisplay = inject(KitchenDisplayService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly tickets = this.kitchenDisplay.tickets;
  protected readonly allTickets = this.kitchenDisplay.allTickets;
  protected readonly currentFilter = this.kitchenDisplay.currentFilter;
  protected readonly connectionStatus = this.kitchenDisplay.connectionStatus;
  protected readonly lastSync = this.kitchenDisplay.lastSync;
  protected readonly nextRefresh = this.kitchenDisplay.nextRefresh;
  protected readonly errorMessage = this.kitchenDisplay.error;

  protected readonly filters: FilterDefinition[] = [
    { key: 'all', label: 'All', description: 'Everything in the queue' },
    { key: 'pending', label: 'Pending', description: 'New tickets awaiting prep' },
    { key: 'prepping', label: 'Cooking', description: 'Actively being prepared' },
    { key: 'ready', label: 'Ready', description: 'Completed and waiting pickup' },
  ];

  protected readonly actionVariants: Record<TicketActionVariant, string> = {
    primary: 'ticket-action ticket-action--primary',
    secondary: 'ticket-action ticket-action--secondary',
    ghost: 'ticket-action ticket-action--ghost',
  };

  protected readonly now = signal(new Date());
  private readonly connectionSummary: Signal<string> = computed(() =>
    this.describeConnection(this.connectionStatus()),
  );
  private readonly currencyFormatter = new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'EUR',
    minimumFractionDigits: 2,
  });
  private audioContext?: AudioContext;

  constructor() {
    const ticker = setInterval(() => this.now.set(new Date()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(ticker));

    const readySubscription = this.kitchenDisplay.ready$.subscribe(() => {
      void this.playReadySound();
    });
    this.destroyRef.onDestroy(() => readySubscription.unsubscribe());
    this.destroyRef.onDestroy(() => {
      if (this.audioContext) {
        void this.audioContext.close();
      }
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      await this.kitchenDisplay.start();
    } catch (error) {
      console.error('Unable to initialise kitchen board', error);
    }
  }

  protected setFilter(filter: TicketFilter): void {
    this.kitchenDisplay.setFilter(filter);
  }

  protected isFilterActive(filter: TicketFilter): boolean {
    return this.currentFilter() === filter;
  }

  protected countFor(filter: TicketFilter): number {
    if (filter === 'all') {
      return this.allTickets().length;
    }

    return this.allTickets().filter((ticket) => this.kitchenDisplay.isTicketVisible(ticket, filter)).length;
  }

  protected formatElapsed(ticket: KitchenTicketViewModel): string {
    const now = this.now().getTime();
    const elapsedSeconds = Math.max(0, Math.floor((now - ticket.createdAtUtc.getTime()) / 1000));
    const minutes = Math.floor(elapsedSeconds / 60);
    const seconds = elapsedSeconds % 60;
    return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  }

  protected formatPickup(time: Date): string {
    return new Intl.DateTimeFormat(undefined, {
      hour: '2-digit',
      minute: '2-digit',
    }).format(time);
  }

  protected formatAmount(amount: number): string {
    return this.currencyFormatter.format(amount);
  }

  protected statusLabel(status: KitchenTicketStatus): string {
    switch (status) {
      case 'Received':
        return 'Pending';
      case 'InPreparation':
        return 'In preparation';
      case 'Ready':
        return 'Ready';
      case 'Completed':
        return 'Completed';
      case 'Cancelled':
        return 'Cancelled';
      default:
        return status;
    }
  }

  protected statusClass(status: KitchenTicketStatus): string {
    switch (status) {
      case 'Ready':
        return 'ticket-status ticket-status--ready';
      case 'InPreparation':
        return 'ticket-status ticket-status--prepping';
      case 'Received':
        return 'ticket-status ticket-status--pending';
      default:
        return 'ticket-status ticket-status--muted';
    }
  }

  protected connectionDescription(): string {
    return this.connectionSummary();
  }

  protected connectionBadgeClass(): string {
    const status = this.connectionStatus();
    if (status === 'connected') {
      return 'kds-connection kds-connection--online';
    }
    if (status === 'connecting') {
      return 'kds-connection kds-connection--connecting';
    }
    return 'kds-connection kds-connection--offline';
  }

  protected describeRelative(date: Date | null): string {
    if (!date) {
      return 'never';
    }

    const diffSeconds = Math.floor((this.now().getTime() - date.getTime()) / 1000);
    if (diffSeconds < 5) {
      return 'just now';
    }
    if (diffSeconds < 60) {
      return `${diffSeconds}s ago`;
    }
    const minutes = Math.floor(diffSeconds / 60);
    return `${minutes}m ago`;
  }

  protected describeCountdown(date: Date | null): string {
    if (!date) {
      return 'calculating…';
    }

    const remainingSeconds = Math.max(0, Math.ceil((date.getTime() - this.now().getTime()) / 1000));
    if (remainingSeconds === 0) {
      return 'refreshing now';
    }
    if (remainingSeconds < 60) {
      return `in ${remainingSeconds}s`;
    }
    const minutes = Math.floor(remainingSeconds / 60);
    const seconds = remainingSeconds % 60;
    return `in ${minutes}m ${seconds.toString().padStart(2, '0')}s`;
  }

  protected showCustomer(ticket: KitchenTicketViewModel): boolean {
    return Boolean(ticket.customerName || ticket.customerPhone);
  }

  protected ticketActions(ticket: KitchenTicketViewModel): TicketAction[] {
    switch (ticket.status) {
      case 'Received':
        return [
          { status: 'InPreparation', label: 'Start prep', variant: 'primary' },
          { status: 'Ready', label: 'Mark ready', variant: 'secondary' },
        ];
      case 'InPreparation':
        return [
          { status: 'Ready', label: 'Mark ready', variant: 'primary' },
          { status: 'Completed', label: 'Complete', variant: 'secondary', confirm: 'Mark order as completed?' },
        ];
      case 'Ready':
        return [
          { status: 'Completed', label: 'Complete', variant: 'primary', confirm: 'Mark order as completed?' },
        ];
      default:
        return [];
    }
  }

  protected actionClass(action: TicketAction): string {
    return this.actionVariants[action.variant] ?? this.actionVariants.primary;
  }

  protected isTicketUpdating(ticket: KitchenTicketViewModel): boolean {
    return this.kitchenDisplay.isTicketUpdating(ticket.orderId);
  }

  protected async triggerAction(ticket: KitchenTicketViewModel, action: TicketAction): Promise<void> {
    if (this.isTicketUpdating(ticket)) {
      return;
    }

    if (action.confirm && !window.confirm(action.confirm)) {
      return;
    }

    const success = await this.kitchenDisplay.updateTicketStatus(ticket.orderId, action.status);
    if (!success) {
      console.warn(`Failed to update order ${ticket.orderCode} to ${action.status}`);
    }
  }

  private describeConnection(status: ConnectionStatus): string {
    switch (status) {
      case 'connected':
        return 'Live updates are online';
      case 'connecting':
        return 'Reconnecting to live updates…';
      default:
        return 'Live updates paused';
    }
  }

  private async playReadySound(): Promise<void> {
    const AudioContextCtor =
      (globalThis.AudioContext ?? (globalThis as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext) ??
      null;
    if (!AudioContextCtor) {
      return;
    }

    if (!this.audioContext) {
      this.audioContext = new AudioContextCtor();
    }

    try {
      if (this.audioContext.state === 'suspended') {
        await this.audioContext.resume();
      }

      const ctx = this.audioContext;
      const oscillator = ctx.createOscillator();
      oscillator.type = 'sine';
      oscillator.frequency.value = 880;

      const gain = ctx.createGain();
      gain.gain.setValueAtTime(0.0001, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.2, ctx.currentTime + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.35);

      oscillator.connect(gain);
      gain.connect(ctx.destination);

      oscillator.start();
      oscillator.stop(ctx.currentTime + 0.4);
    } catch (error) {
      console.debug('Unable to play ready notification', error);
    }
  }
}
