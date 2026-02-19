import axios from 'axios';

// v2 — relative URLs, proxied through Vite
const api = axios.create({
  baseURL: '/api',
  timeout: 10000,
});

// ==================== Types ====================

export interface DashboardStats {
  customers: number;
  meteringPoints: number;
  activeSupplies: number;
  activeProcesses: number;
  unprocessedInbox: number;
  unsentOutbox: number;
  settlements: {
    calculated: number;
    invoiced: number;
    adjusted: number;
    corrections: number;
    totalAmount: number;
  };
}

export interface SupplierIdentity {
  id: string;
  gln: string;
  name: string;
  cvr: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface Customer {
  id: string;
  name: string;
  cpr: string | null;
  cvr: string | null;
  email: string | null;
  phone: string | null;
  isPrivate: boolean;
  isCompany: boolean;
  supplierIdentityId: string;
  supplierGln?: string;
  supplierName?: string;
  createdAt: string;
}

export interface CustomerDetail extends Customer {
  address: AddressDto | null;
  supplies: SupplyRef[];
}

export interface AddressDto {
  streetName: string;
  buildingNumber: string;
  floor: string | null;
  suite: string | null;
  postCode: string;
  cityName: string;
}

export interface SupplyRef {
  id: string;
  meteringPointId: string;
  gsrn?: string;
  customerId?: string;
  customerName?: string;
  supplyStart: string;
  supplyEnd: string | null;
  isActive: boolean;
}

export interface MeteringPoint {
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

export interface MeteringPointDetail extends MeteringPoint {
  address: AddressDto | null;
  supplies: SupplyRef[];
  time_series: TimeSeriesRef[];
}

export interface TimeSeriesRef {
  id: string;
  periodStart: string;
  periodEnd: string | null;
  resolution: string;
  version: number;
  isLatest: boolean;
  receivedAt: string;
}

export interface Supply {
  id: string;
  meteringPointId: string;
  gsrn: string;
  customerId: string;
  customerName: string;
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
  meteringPointGsrn: string | null;
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

export interface Settlement {
  id: string;
  meteringPointId: string;
  supplyId: string;
  periodStart: string;
  periodEnd: string | null;
  totalEnergyKwh: number;
  totalAmount: number;
  currency: string;
  status: string;
  isCorrection: boolean;
  previousSettlementId: string | null;
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

export interface PriceSummary {
  id: string;
  chargeId: string;
  ownerGln: string;
  type: string;
  description: string;
  validFrom: string;
  validTo: string | null;
  vatExempt: boolean;
  isTax: boolean;
  isPassThrough: boolean;
  priceResolution: string | null;
  pricePointCount: number;
  linkedMeteringPoints: number;
}

export interface PricePointDto {
  timestamp: string;
  price: number;
}

export interface PriceLinkDto {
  id: string;
  meteringPointId: string;
  gsrn: string;
  linkFrom: string;
  linkTo: string | null;
}

export interface PriceDetail extends Omit<PriceSummary, 'pricePointCount' | 'linkedMeteringPoints'> {
  pricePoints: PricePointDto[];
  totalPricePoints: number;
  linkedMeteringPoints: PriceLinkDto[];
}

// ==================== API Calls ====================

// Dashboard
export const getDashboard = () => api.get<DashboardStats>('/dashboard');

// Supplier Identities
export const getSupplierIdentities = () => api.get<SupplierIdentity[]>('/supplier-identities');
export const createSupplierIdentity = (data: { gln: string; name: string; cvr?: string; isActive?: boolean }) =>
  api.post('/supplier-identities', data);
export const patchSupplierIdentity = (id: string, data: { isActive?: boolean; name?: string }) =>
  api.patch(`/supplier-identities/${id}`, data);

// Customers
export const getCustomers = () => api.get<Customer[]>('/customers');
export const getCustomer = (id: string) => api.get<CustomerDetail>(`/customers/${id}`);
export const createCustomer = (data: {
  name: string; cpr?: string; cvr?: string; supplierIdentityId: string;
  email?: string; phone?: string; address?: AddressDto;
}) => api.post('/customers', data);

// MeteringPoints
export const getMeteringPoints = () => api.get<MeteringPoint[]>('/metering-points');
export const getMeteringPoint = (id: string) => api.get<MeteringPointDetail>(`/metering-points/${id}`);
export const createMeteringPoint = (data: {
  gsrn: string; type: string; art: string; settlementMethod: string;
  resolution: string; gridArea: string; gridCompanyGln: string; address?: AddressDto;
}) => api.post('/metering-points', data);

// Supplies
export const getSupplies = () => api.get<Supply[]>('/supplies');
export const createSupply = (data: {
  meteringPointId: string; customerId: string;
  supplyStart: string; supplyEnd?: string;
}) => api.post('/supplies', data);

// Processes
export const getProcesser = () => api.get<BrsProcess[]>('/processes');
export const getProcess = (id: string) => api.get<any>(`/processes/${id}`);

// Settlements
export const getSettlements = () => api.get<Settlement[]>('/settlements');

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
export const retryOutboxMessage = (id: string) => api.post(`/outbox/${id}/retry`);

// Prices
export const getPrices = () => api.get<PriceSummary[]>('/prices');
export const getPrice = (id: string) => api.get<PriceDetail>(`/prices/${id}`);

// ==================== BRS Process Actions ====================

// BRS-002: End of Supply
export const initiateEndOfSupply = (data: { gsrn: string; desiredEndDate: string; reason?: string }) =>
  api.post('/processes/end-of-supply', data);

// BRS-010: Move-Out
export const initiateMoveOut = (data: { gsrn: string; effectiveDate: string }) =>
  api.post('/processes/move-out', data);

// BRS-015: Customer Update
export const sendCustomerUpdate = (data: {
  gsrn: string; effectiveDate: string; customerName: string;
  cpr?: string; cvr?: string; email?: string; phone?: string;
  address?: AddressDto;
}) => api.post('/processes/customer-update', data);

// BRS-003: Incorrect Supplier Switch
export const initiateIncorrectSwitch = (data: { gsrn: string; switchDate: string; reason?: string }) =>
  api.post('/processes/incorrect-switch', data);

// BRS-011: Incorrect Move
export const initiateIncorrectMove = (data: { gsrn: string; moveDate: string; moveType: string; reason?: string }) =>
  api.post('/processes/incorrect-move', data);

// BRS-005: Request Master Data
export const requestMasterData = (data: { gsrn: string }) =>
  api.post('/processes/request-master-data', data);

// BRS-039: Service Request
export const createServiceRequest = (data: { gsrn: string; serviceType: string; requestedDate: string; reason?: string }) =>
  api.post('/processes/service-request', data);

// BRS-041: Electrical Heating
export const toggleElectricalHeating = (data: { gsrn: string; action: string; effectiveDate: string }) =>
  api.post('/processes/electrical-heating', data);

// BRS-024: Request Yearly Sum
export const requestYearlySum = (data: { gsrn: string }) =>
  api.post('/processes/request-yearly-sum', data);

// BRS-025: Request Metered Data
export const requestMeteredData = (data: { gsrn: string; startDate: string; endDate: string }) =>
  api.post('/processes/request-metered-data', data);

// BRS-034: Request Prices
export const requestPrices = (data: { startDate: string; endDate?: string; priceOwnerGln?: string; priceType?: string; requestType?: string }) =>
  api.post('/processes/request-prices', data);

// BRS-038: Request Charge Links
export const requestChargeLinks = (data: { gsrn: string; startDate: string; endDate?: string }) =>
  api.post('/processes/request-charge-links', data);

// BRS-023: Request Aggregated Data
export const requestAggregatedData = (data: { gridArea: string; startDate: string; endDate: string; meteringPointType?: string; processType?: string }) =>
  api.post('/processes/request-aggregated-data', data);

// BRS-027: Request Wholesale Settlement
export const requestWholesaleSettlement = (data: { gridArea: string; startDate: string; endDate: string; energySupplierGln?: string }) =>
  api.post('/processes/request-wholesale-settlement', data);

export default api;
