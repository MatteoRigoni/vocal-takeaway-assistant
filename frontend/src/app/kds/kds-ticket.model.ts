export type KitchenTicketStatus = 'Received' | 'InPreparation' | 'Ready' | 'Completed' | 'Cancelled' | string;

export interface KdsOrderItemDto {
  id: number;
  productName: string;
  variantName: string | null;
  modifiers: string[];
  quantity: number;
}

export interface KdsOrderTicketDto {
  orderId: number;
  orderCode: string;
  status: KitchenTicketStatus;
  createdAtUtc: string;
  pickupAtUtc: string;
  customerName: string | null;
  customerPhone: string | null;
  notes: string | null;
  totalAmount: number;
  items: KdsOrderItemDto[];
}

export interface KitchenTicketItem {
  id: number;
  productName: string;
  variantName?: string | null;
  modifiers: string[];
  quantity: number;
}

export interface KitchenTicketViewModel {
  orderId: number;
  orderCode: string;
  status: KitchenTicketStatus;
  createdAtUtc: Date;
  pickupAtUtc: Date;
  customerName?: string | null;
  customerPhone?: string | null;
  notes?: string | null;
  totalAmount: number;
  items: KitchenTicketItem[];
}

export type TicketFilter = 'all' | 'pending' | 'prepping' | 'ready';
