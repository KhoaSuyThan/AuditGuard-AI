export interface ThreeWayRowDto {
  poItemName: string;
  poQuantity: number;
  poUnitPrice: number;
  grnQuantityReceived: number;
  invoiceItemName: string;
  invoiceQuantity: number;
  invoiceUnitPrice: number;
  similarityScore: number;
  matchStatus: 'MATCHED' | 'PRICE_MISMATCH' | 'QTY_MISMATCH' | 'UNMATCHED';
}

export interface AuditDiscrepancyDto {
  id: string;
  discrepancyType: string;
  severity: 'Critical' | 'Warning' | 'Info' | string;
  itemName?: string;
  expectedValue?: string;
  actualValue?: string;
  financialImpact: number;
  description: string;
}

export interface AuditSessionDto {
  id: string;
  sessionCode: string;
  status: 'PENDING' | 'PASSED' | 'FLAGGED' | 'REJECTED' | string;
  totalFinancialDiff: number;
  discrepancyCount: number;
  poNumber?: string;
  grnNumber?: string;
  invoiceNumber?: string;
  vendorName?: string;
  createdAt: string;
  discrepancies: AuditDiscrepancyDto[];
  comparisonRows?: ThreeWayRowDto[];
  notes?: string;
}

export interface ScenarioInfoDto {
  scenarioId: string;
  folderName: string;
  name: string;
  description: string;
  expectedResult: string;
  financialImpactVnd: number;
}

export interface DisputeEmailDto {
  recipientEmail: string;
  subject: string;
  bodyHtml: string;
  bodyPlainText: string;
}

export interface ProgressState {
  stage: string;
  percent: number;
  message: string;
  isProcessing: boolean;
  error?: string;
}
