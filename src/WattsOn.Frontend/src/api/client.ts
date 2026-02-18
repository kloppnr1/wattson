import axios from 'axios';

const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5100';

const api = axios.create({
  baseURL: `${API_URL}/api`,
  timeout: 10000,
});

// ==================== Types ====================

export interface DashboardStats {
  kunder: number;
  målepunkter: number;
  aktiveLeverancer: number;
  aktiveProcesser: number;
  ubehandledeInbox: number;
  uafsendeOutbox: number;
  afregninger: {
    beregnede: number;
    fakturerede: number;
    justerede: number;
    korrektioner: number;
    totalBeløb: number;
  };
}

export interface Aktør {
  id: string;
  gln: string;
  name: string;
  role: string;
  cvr: string | null;
  isOwn: boolean;
  createdAt: string;
}

export interface Kunde {
  id: string;
  name: string;
  cpr: string | null;
  cvr: string | null;
  email: string | null;
  phone: string | null;
  isPrivate: boolean;
  isCompany: boolean;
  createdAt: string;
}

export interface KundeDetail extends Kunde {
  address: AddressDto | null;
  leverancer: LeveranceRef[];
}

export interface AddressDto {
  streetName: string;
  buildingNumber: string;
  floor: string | null;
  suite: string | null;
  postCode: string;
  cityName: string;
}

export interface LeveranceRef {
  id: string;
  målepunktId: string;
  gsrn?: string;
  kundeId?: string;
  kundeNavn?: string;
  supplyStart: string;
  supplyEnd: string | null;
  isActive: boolean;
}

export interface Målepunkt {
  id: string;
  gsrn: string;
  type: string;
  art: string;
  settlementMethod: string;
  resolution: string;
  connectionState: string;
  gridArea: string;
  gridCompanyGln: string;
  hasActiveSupply: boolean;
  createdAt: string;
}

export interface MålepunktDetail extends Målepunkt {
  address: AddressDto | null;
  leverancer: LeveranceRef[];
  tidsserier: TidsserieRef[];
}

export interface TidsserieRef {
  id: string;
  periodStart: string;
  periodEnd: string | null;
  resolution: string;
  version: number;
  isLatest: boolean;
  receivedAt: string;
}

export interface Leverance {
  id: string;
  målepunktId: string;
  gsrn: string;
  kundeId: string;
  kundeNavn: string;
  supplyStart: string;
  supplyEnd: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface BrsProcess {
  id: string;
  transactionId: string | null;
  processType: string;
  role: string;
  status: string;
  currentState: string;
  målepunktGsrn: string | null;
  effectiveDate: string | null;
  startedAt: string;
  completedAt: string | null;
  errorMessage: string | null;
}

export interface InboxMessage {
  id: string;
  messageId: string;
  documentType: string;
  businessProcess: string | null;
  senderGln: string;
  receiverGln: string;
  receivedAt: string;
  isProcessed: boolean;
  processedAt: string | null;
  processingError: string | null;
  processingAttempts: number;
}

export interface Afregning {
  id: string;
  målepunktId: string;
  leveranceId: string;
  periodStart: string;
  periodEnd: string | null;
  totalEnergyKwh: number;
  totalAmount: number;
  currency: string;
  status: string;
  isCorrection: boolean;
  previousAfregningId: string | null;
  externalInvoiceReference: string | null;
  invoicedAt: string | null;
  calculatedAt: string;
}

export interface SettlementDocumentLine {
  lineNumber: number;
  description: string;
  quantity: number;
  quantityUnit: string;
  unitPrice: number;
  lineAmount: number;
  chargeId: string | null;
  chargeOwnerGln: string | null;
  taxCategory: string;
  taxPercent: number;
  taxAmount: number;
}

export interface TaxSummaryItem {
  taxCategory: string;
  taxPercent: number;
  taxableAmount: number;
  taxAmount: number;
}

export interface SettlementDocument {
  documentType: string;
  documentId: string;
  originalDocumentId: string | null;
  settlementId: string;
  status: string;
  period: { start: string; end: string | null };
  seller: { name: string; identifier: string | null; identifierScheme: string; glnNumber: string };
  buyer: {
    name: string; identifier: string | null; identifierScheme: string;
    address: AddressDto | null;
  };
  meteringPoint: { gsrn: string; gridArea: string };
  lines: SettlementDocumentLine[];
  taxSummary: TaxSummaryItem[];
  totalExclVat: number;
  totalVat: number;
  totalInclVat: number;
  currency: string;
  calculatedAt: string;
  externalInvoiceReference: string | null;
  invoicedAt: string | null;
}

// ==================== API Calls ====================

// Dashboard
export const getDashboard = () => api.get<DashboardStats>('/dashboard');

// Aktører
export const getAktører = () => api.get<Aktør[]>('/aktører');
export const createAktør = (data: { gln: string; name: string; role: string; cvr?: string; isOwn?: boolean }) =>
  api.post('/aktører', data);

// Kunder
export const getKunder = () => api.get<Kunde[]>('/kunder');
export const getKunde = (id: string) => api.get<KundeDetail>(`/kunder/${id}`);
export const createKunde = (data: {
  name: string; cpr?: string; cvr?: string;
  email?: string; phone?: string; address?: AddressDto;
}) => api.post('/kunder', data);

// Målepunkter
export const getMålepunkter = () => api.get<Målepunkt[]>('/målepunkter');
export const getMålepunkt = (id: string) => api.get<MålepunktDetail>(`/målepunkter/${id}`);
export const createMålepunkt = (data: {
  gsrn: string; type: string; art: string; settlementMethod: string;
  resolution: string; gridArea: string; gridCompanyGln: string; address?: AddressDto;
}) => api.post('/målepunkter', data);

// Leverancer
export const getLeverancer = () => api.get<Leverance[]>('/leverancer');
export const createLeverance = (data: {
  målepunktId: string; kundeId: string; aktørId: string;
  supplyStart: string; supplyEnd?: string;
}) => api.post('/leverancer', data);

// Processer
export const getProcesser = () => api.get<BrsProcess[]>('/processer');
export const getProcess = (id: string) => api.get<any>(`/processer/${id}`);

// Afregninger
export const getAfregninger = () => api.get<Afregning[]>('/afregninger');

// Settlement Documents
export const getSettlementDocuments = (status?: string) =>
  api.get<SettlementDocument[]>('/settlement-documents', { params: { status } });
export const getSettlementDocument = (id: string) =>
  api.get<SettlementDocument>(`/settlement-documents/${id}`);
export const confirmSettlement = (id: string, externalInvoiceReference: string) =>
  api.post(`/settlement-documents/${id}/confirm`, { externalInvoiceReference });

// Inbox / Outbox
export const getInbox = (unprocessed?: boolean) => api.get<InboxMessage[]>('/inbox', { params: { unprocessed } });
export const getOutbox = (unsent?: boolean) => api.get<any[]>('/outbox', { params: { unsent } });

export default api;
