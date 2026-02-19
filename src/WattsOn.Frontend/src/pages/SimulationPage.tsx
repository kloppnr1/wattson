import { useState, useCallback, useRef, useEffect } from 'react';
import {
  Card, Row, Col, Button, Space, Typography, Steps, Tag, Descriptions,
  Alert, Spin, Input, DatePicker, Switch, Divider, Collapse, Result,
  Segmented, Select, Empty,
} from 'antd';
import {
  SwapOutlined, PlayCircleOutlined,
  LoadingOutlined,
  UserOutlined, CalculatorOutlined,
  ReloadOutlined, ExperimentOutlined, LoginOutlined,
  LogoutOutlined, UserDeleteOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import api from '../api/client';
import type { Supply } from '../api/client';
import { getSupplies } from '../api/client';

const { Text, Title, Paragraph } = Typography;

// --- Types ---

type ScenarioType = 'supplier-in' | 'supplier-out' | 'move-in' | 'move-out';

interface SimulationStep {
  key: string;
  title: string;
  description: string;
  status: 'waiting' | 'running' | 'done' | 'error';
  detail?: string;
  timestamp?: string;
}

interface SimulationResult {
  processId: string;
  transactionId: string;
  status: string;
  currentState: string;
  gsrn: string;
  customerName: string;
  customerId: string;
  meteringPointId?: string;
  newSupplyId?: string | null;
  endedSupplyId?: string | null;
  effectiveDate: string;
  priceLinksCreated?: number;
  timeSeriesGenerated?: boolean;
  totalEnergyKwh?: number | null;
  message: string;
}

interface SettlementLine {
  description: string;
  quantityKwh: number;
  unitPrice: number;
  amount: number;
  currency: string;
}

interface SettlementPollResult {
  found: boolean;
  id: string;
  gsrn: string;
  periodStart: string;
  periodEnd: string;
  totalEnergyKwh: number;
  totalAmount: number;
  currency: string;
  status: string;
  isCorrection: boolean;
  timeSeriesVersion: number;
  calculatedAt: string;
  lines: SettlementLine[];
}

// --- Danish name generators ---

const FIRST_NAMES = [
  'Anders', 'Mette', 'Lars', 'Sofie', 'Peter', 'Anna', 'Søren', 'Katrine',
  'Mikkel', 'Louise', 'Thomas', 'Camilla', 'Henrik', 'Ida', 'Rasmus', 'Emma',
  'Christian', 'Julie', 'Nikolaj', 'Frederikke', 'Jonas', 'Clara', 'Viktor', 'Maja',
];
const LAST_NAMES = [
  'Jensen', 'Nielsen', 'Hansen', 'Pedersen', 'Andersen', 'Christensen',
  'Larsen', 'Sørensen', 'Rasmussen', 'Jørgensen', 'Petersen', 'Madsen',
];
const STREETS = [
  'Vestergade', 'Nørregade', 'Søndergade', 'Østergade', 'Algade',
  'Bredgade', 'Strandvejen', 'Parallelvej', 'Skovvej', 'Rosenvænget',
  'Åboulevarden', 'Vester Allé', 'Frederiks Allé', 'Banegårdsgade', 'Skolegade',
];
const CITIES: [string, string][] = [
  ['8000', 'Aarhus C'], ['8200', 'Aarhus N'], ['8210', 'Aarhus V'],
  ['2100', 'København Ø'], ['2200', 'København N'], ['1000', 'København K'],
  ['5000', 'Odense C'], ['9000', 'Aalborg'], ['7100', 'Vejle'], ['6000', 'Kolding'],
];

function randomItem<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function generateGsrn(): string {
  const base = '571313' + Array.from({ length: 11 }, () => Math.floor(Math.random() * 10)).join('');
  const digits = base.split('').map(Number);
  const weights = [1,3,1,3,1,3,1,3,1,3,1,3,1,3,1,3,1];
  const sum = digits.reduce((s, d, i) => s + d * weights[i], 0);
  const check = (10 - (sum % 10)) % 10;
  return base + check;
}

function generateCpr(): string {
  const day = String(Math.floor(Math.random() * 28) + 1).padStart(2, '0');
  const month = String(Math.floor(Math.random() * 12) + 1).padStart(2, '0');
  const year = String(Math.floor(Math.random() * 30) + 60).padStart(2, '0');
  const seq = String(Math.floor(Math.random() * 9000) + 1000);
  return `${day}${month}${year}${seq}`;
}

const scenarioConfig: Record<ScenarioType, {
  label: string; icon: React.ReactNode; color: string;
  description: string; needsExistingSupply: boolean;
}> = {
  'supplier-in': {
    label: 'Leverandørskift · Indgående',
    icon: <LoginOutlined />,
    color: '#059669',
    description: 'Vi overtager en customer fra en anden elleverandør',
    needsExistingSupply: false,
  },
  'supplier-out': {
    label: 'Leverandørskift · Udgående',
    icon: <UserDeleteOutlined />,
    color: '#e11d48',
    description: 'En anden leverandør overtager vores customer',
    needsExistingSupply: true,
  },
  'move-in': {
    label: 'Tilflytning',
    icon: <LoginOutlined />,
    color: '#7c3aed',
    description: 'Ny customer flytter ind på et metering_point',
    needsExistingSupply: false,
  },
  'move-out': {
    label: 'Fraflytning',
    icon: <LogoutOutlined />,
    color: '#d97706',
    description: 'Customer fraflytter — supply afsluttes og slutsettlement',
    needsExistingSupply: true,
  },
};

// --- Main Component ---

export default function SimulationPage() {
  const navigate = useNavigate();

  // Scenario
  const [scenario, setScenario] = useState<ScenarioType>('supplier-in');
  const config = scenarioConfig[scenario];

  // Form state — new customer
  const [customerName, setCustomerName] = useState('');
  const [gsrn, setGsrn] = useState('');
  const [cpr, setCpr] = useState('');
  const [effectiveDate, setEffectiveDate] = useState<Dayjs | null>(dayjs().startOf('month'));
  const [generateConsumption, setGenerateConsumption] = useState(true);
  const [street, setStreet] = useState('');
  const [buildingNumber, setBuildingNumber] = useState('');
  const [postCode, setPostCode] = useState('');
  const [city, setCity] = useState('');

  // Form state — existing supply (for outgoing/move-out)
  const [activeSupplies, setActiveSupplies] = useState<Supply[]>([]);
  const [selectedSupplyId, setSelectedSupplyId] = useState<string | null>(null);
  const [loadingSupplies, setLoadingSupplies] = useState(false);

  // Simulation state
  const [running, setRunning] = useState(false);
  const [steps, setSteps] = useState<SimulationStep[]>([]);
  const [result, setResult] = useState<SimulationResult | null>(null);
  const [settlement, setSettlement] = useState<SettlementPollResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef(false);

  // Load active supplies for outgoing scenarios
  useEffect(() => {
    if (config.needsExistingSupply) {
      setLoadingSupplies(true);
      getSupplies()
        .then(res => {
          const active = res.data.filter((l: Supply) => l.isActive);
          setActiveSupplies(active);
          if (active.length > 0 && !selectedSupplyId) {
            setSelectedSupplyId(active[0].id);
          }
        })
        .finally(() => setLoadingSupplies(false));
    }
  }, [scenario]);

  const randomize = useCallback(() => {
    const first = randomItem(FIRST_NAMES);
    const last = randomItem(LAST_NAMES);
    setCustomerName(`${first} ${last}`);
    setGsrn(generateGsrn());
    setCpr(generateCpr());
    setStreet(randomItem(STREETS));
    setBuildingNumber(String(Math.floor(Math.random() * 150) + 1));
    const [pc, ct] = randomItem(CITIES);
    setPostCode(pc);
    setCity(ct);
    setEffectiveDate(dayjs().startOf('month'));
  }, []);

  // Initialize random data
  useState(() => { randomize(); });

  const updateStep = (key: string, update: Partial<SimulationStep>) => {
    setSteps(prev => prev.map(s => s.key === key ? { ...s, ...update } : s));
  };

  const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

  const now = () => new Date().toLocaleTimeString('da-DK');

  // --- Build steps for each scenario ---

  const buildSteps = (): SimulationStep[] => {
    const base: SimulationStep[] = [];

    if (scenario === 'supplier-in') {
      base.push(
        { key: 'request', title: 'Anmodning om leverandørskift', description: 'Sender RSM-001/E03 til DataHub', status: 'waiting' },
        { key: 'validate', title: 'DataHub validering', description: 'Validerer GSRN, CPR, tilgængelighed', status: 'waiting' },
        { key: 'confirm', title: 'Bekræftelse received', description: 'DataHub godkender leverandørskift', status: 'waiting' },
        { key: 'masterdata', title: 'Stamdata & price_link', description: 'Modtager customer- og metering_pointdata', status: 'waiting' },
        { key: 'execute', title: 'Leverandørskift completed', description: 'Ny supply aktiv', status: 'waiting' },
      );
    } else if (scenario === 'supplier-out') {
      base.push(
        { key: 'notification', title: 'Stop-of-supply received', description: 'DataHub sender RSM-004/E03', status: 'waiting' },
        { key: 'acknowledge', title: 'Besked bekræftet', description: 'Vi modtager og behandler notifikation', status: 'waiting' },
        { key: 'end-supply', title: 'Supply afsluttet', description: 'Supply afsluttes pr. effektiv dato', status: 'waiting' },
        { key: 'final', title: 'Proces completed', description: 'Customer overdraget til ny leverandør', status: 'waiting' },
      );
    } else if (scenario === 'move-in') {
      base.push(
        { key: 'request', title: 'Tilflytningsanmodning', description: 'Sender RSM-001/E65 til DataHub', status: 'waiting' },
        { key: 'validate', title: 'DataHub validering', description: 'Validerer GSRN, CPR, tilgængelighed', status: 'waiting' },
        { key: 'confirm', title: 'Bekræftelse received', description: 'DataHub godkender tilflytning', status: 'waiting' },
        { key: 'masterdata', title: 'Stamdata & price_link', description: 'Modtager metering_pointdata', status: 'waiting' },
        { key: 'execute', title: 'Tilflytning completed', description: 'Ny supply aktiv', status: 'waiting' },
      );
    } else if (scenario === 'move-out') {
      base.push(
        { key: 'request', title: 'Fraflytningsanmodning', description: 'Sender anmodning til DataHub', status: 'waiting' },
        { key: 'confirm', title: 'DataHub bekræftelse', description: 'Fraflytning godkendt', status: 'waiting' },
        { key: 'end-supply', title: 'Supply afsluttet', description: 'Supply afsluttes pr. fraflytningsdato', status: 'waiting' },
        { key: 'final', title: 'Afpending slutsettlement', description: 'SettlementWorker beregner automatisk', status: 'waiting' },
      );
    }

    // Add consumption + settlement steps for incoming scenarios
    if (!config.needsExistingSupply && generateConsumption) {
      base.push(
        { key: 'timeseries', title: 'Forbrugsdata received', description: 'Timemålinger indlæst fra DataHub', status: 'waiting' },
        { key: 'settlement', title: 'Settlement beregnet', description: 'SettlementWorker afregner automatisk', status: 'waiting' },
      );
    }

    return base;
  };

  // --- Poll for settlement by metering point ---
  const pollForSettlement = async (meteringPointId: string) => {
    for (let attempt = 0; attempt < 15; attempt++) {
      if (abortRef.current) break;
      await sleep(5000);
      try {
        const res = await api.get<SettlementPollResult>(`/settlements/by-metering-point/${meteringPointId}`);
        if (res.data.found) {
          setSettlement(res.data);
          return res.data;
        }
      } catch { /* keep polling */ }
      updateStep('settlement', {
        status: 'running',
        detail: `Waiting for Settlement Engine... (${(attempt + 1) * 5}s)`,
      });
    }
    return null;
  };

  // --- Run simulation ---
  const runSimulation = async () => {
    abortRef.current = false;
    setRunning(true);
    setResult(null);
    setSettlement(null);
    setError(null);

    const initialSteps = buildSteps();
    setSteps(initialSteps);

    try {
      if (scenario === 'supplier-in' || scenario === 'move-in') {
        await runIncomingFlow();
      } else {
        await runOutgoingFlow();
      }
    } catch (err: any) {
      const msg = err.response?.data?.toString?.() || err.message || 'Ukendt error';
      setError(msg);
      setSteps(prev => prev.map(s =>
        s.status === 'running' ? { ...s, status: 'error', detail: msg } : s
      ));
    } finally {
      setRunning(false);
    }
  };

  // --- Incoming flow (supplier-in, move-in) ---
  const runIncomingFlow = async () => {
    const isSupplierChange = scenario === 'supplier-in';
    const endpoint = isSupplierChange ? '/simulation/supplier-change' : '/simulation/move-in';

    // Step 1: Request
    updateStep('request', { status: 'running', detail: `Sender anmodning for ${customerName}...`, timestamp: now() });
    await sleep(800);
    updateStep('request', { status: 'done', detail: isSupplierChange ? 'RSM-001/E03 afsendt' : 'RSM-001/E65 afsendt' });

    // Step 2: Validate
    updateStep('validate', { status: 'running', detail: `Validerer CPR ${cpr} mod GSRN ${gsrn}...`, timestamp: now() });
    await sleep(1000);
    updateStep('validate', { status: 'done', detail: 'Alle valideringer bestået' });

    // Step 3: Confirm
    updateStep('confirm', { status: 'running', detail: 'Afpending DataHub bekræftelse...', timestamp: now() });
    await sleep(700);
    updateStep('confirm', { status: 'done', detail: 'Godkendt af DataHub' });

    // Step 4: Masterdata — actual API call
    updateStep('masterdata', { status: 'running', detail: 'Opretter stamdata og price_links...', timestamp: now() });

    const emailName = customerName.toLowerCase().replace(/\s+/g, '.').replace(/[æ]/g,'ae').replace(/[ø]/g,'oe').replace(/[å]/g,'aa');
    const response = await api.post<SimulationResult>(endpoint, {
      gsrn,
      effectiveDate: effectiveDate?.toISOString(),
      customerName,
      cprNumber: cpr,
      email: `${emailName}@example.dk`,
      address: { streetName: street, buildingNumber, postCode, cityName: city },
      generateConsumption,
    });

    const data = response.data;
    setResult(data);

    await sleep(500);
    updateStep('masterdata', { status: 'done', detail: `Customer created, ${data.priceLinksCreated ?? 0} prices tilknyttet` });

    // Step 5: Execute
    updateStep('execute', { status: 'running', detail: 'Opretter supply...', timestamp: now() });
    await sleep(600);
    updateStep('execute', {
      status: 'done',
      detail: `Supply aktiv fra ${dayjs(data.effectiveDate).format('D. MMM YYYY')}` +
        (data.endedSupplyId ? ' (tidligere supply afsluttet)' : ''),
    });

    // Steps 6-7: Time series + settlement
    if (generateConsumption && data.timeSeriesGenerated) {
      updateStep('timeseries', { status: 'running', detail: `Indlæser ${data.totalEnergyKwh?.toFixed(1)} kWh...`, timestamp: now() });
      await sleep(800);
      updateStep('timeseries', { status: 'done', detail: `${data.totalEnergyKwh?.toFixed(1)} kWh timemålinger indlæst` });

      updateStep('settlement', { status: 'running', detail: 'Waiting for Settlement Engine...', timestamp: now() });
      const doc = await pollForSettlement(data.meteringPointId!);
      if (doc) {
        const totalDKK = doc.totalAmount.toLocaleString('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        updateStep('settlement', {
          status: 'done',
          detail: `${doc.totalEnergyKwh.toFixed(1)} kWh → ${totalDKK} DKK (${doc.lines.length} price lines)`,
        });
      } else {
        updateStep('settlement', { status: 'done', detail: 'Timeout — Settlement Engine has not processed yet' });
      }
    }
  };

  // --- Outgoing flow (supplier-out, move-out) ---
  const runOutgoingFlow = async () => {
    if (!selectedSupplyId) throw new Error('Vælg en aktiv supply');
    const isSupplierOut = scenario === 'supplier-out';
    const endpoint = isSupplierOut ? '/simulation/supplier-change-outgoing' : '/simulation/move-out';

    const selectedLev = activeSupplies.find(l => l.id === selectedSupplyId);

    if (isSupplierOut) {
      // Step 1: Notification received
      updateStep('notification', {
        status: 'running',
        detail: `DataHub sender stop-of-supply for ${selectedLev?.customerName ?? 'customer'}...`,
        timestamp: now(),
      });
      await sleep(1000);
      updateStep('notification', { status: 'done', detail: 'RSM-004/E03 received fra DataHub' });

      // Step 2: Acknowledge
      updateStep('acknowledge', { status: 'running', detail: 'Behandler notifikation...', timestamp: now() });
    } else {
      // Step 1: Request
      updateStep('request', {
        status: 'running',
        detail: `Sender fraflytningsanmodning for ${selectedLev?.customerName ?? 'customer'}...`,
        timestamp: now(),
      });
      await sleep(800);
      updateStep('request', { status: 'done', detail: 'Anmodning afsendt' });

      // Step 2: Confirm
      updateStep('confirm', { status: 'running', detail: 'Afpending DataHub bekræftelse...', timestamp: now() });
    }

    // API call
    const response = await api.post<SimulationResult>(endpoint, {
      supplyId: selectedSupplyId,
      effectiveDate: effectiveDate?.toISOString(),
      ...(isSupplierOut ? { newSupplierGln: '5790000000005' } : {}),
    });

    const data = response.data;
    setResult(data);

    await sleep(600);
    if (isSupplierOut) {
      updateStep('acknowledge', { status: 'done', detail: 'Notifikation processed' });
    } else {
      updateStep('confirm', { status: 'done', detail: 'DataHub godkendt' });
    }

    // Step 3: End supply
    updateStep('end-supply', {
      status: 'running',
      detail: `Afslutter supply pr. ${dayjs(data.effectiveDate).format('D. MMM YYYY')}...`,
      timestamp: now(),
    });
    await sleep(700);
    updateStep('end-supply', {
      status: 'done',
      detail: `Supply afsluttet for ${data.customerName}`,
    });

    // Step 4: Final
    updateStep('final', { status: 'running', detail: 'Afpending slutsettlement...', timestamp: now() });
    await sleep(500);
    updateStep('final', {
      status: 'done',
      detail: isSupplierOut
        ? `${data.customerName} overdraget til ny leverandør`
        : `${data.customerName} fraflyttet — slutsettlement beregnes`,
    });
  };

  const reset = () => {
    abortRef.current = true;
    setRunning(false);
    setSteps([]);
    setResult(null);
    setSettlement(null);
    setError(null);
    setSelectedSupplyId(null);
    randomize();
  };

  const currentStepIndex = steps.findIndex(s => s.status === 'running');
  const allDone = steps.length > 0 && steps.every(s => s.status === 'done');

  void 0; // formatDKK removed — was unused

  const canRun = config.needsExistingSupply
    ? !!selectedSupplyId && effectiveDate
    : !!customerName && !!gsrn && !!cpr && effectiveDate;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <Row align="middle" justify="space-between">
        <Col>
          <Space align="center" size={12}>
            <ExperimentOutlined style={{ fontSize: 24, color: '#7c3aed' }} />
            <div>
              <Title level={3} style={{ margin: 0 }}>Simulation</Title>
              <Text type="secondary">Kør realistiske scenarier og se systemet arbejde</Text>
            </div>
          </Space>
        </Col>
      </Row>

      {/* Scenario selector */}
      <Segmented
        block
        value={scenario}
        onChange={(v) => { setScenario(v as ScenarioType); reset(); }}
        disabled={running}
        options={[
          { value: 'supplier-in', icon: <LoginOutlined />, label: 'Skift · Ind' },
          { value: 'supplier-out', icon: <UserDeleteOutlined />, label: 'Skift · Ud' },
          { value: 'move-in', icon: <LoginOutlined />, label: 'Tilflytning' },
          { value: 'move-out', icon: <LogoutOutlined />, label: 'Fraflytning' },
        ]}
        style={{ marginBottom: 0 }}
      />

      <Row gutter={[24, 24]}>
        {/* Left: Scenario config */}
        <Col xs={24} lg={10}>
          <Card
            title={
              <Space>
                <span style={{ color: config.color }}>{config.icon}</span>
                <span>{config.label}</span>
              </Space>
            }
            extra={
              !config.needsExistingSupply && (
                <Button size="small" icon={<ReloadOutlined />} onClick={randomize} disabled={running}>
                  Tilfældig
                </Button>
              )
            }
            style={{ borderRadius: 12 }}
          >
            <Paragraph type="secondary" style={{ marginBottom: 16 }}>{config.description}</Paragraph>

            <Space direction="vertical" size={16} style={{ width: '100%' }}>
              {config.needsExistingSupply ? (
                /* Outgoing: select existing supply */
                <>
                  <div>
                    <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                      Vælg aktiv supply
                    </Text>
                    {loadingSupplies ? <Spin size="small" style={{ marginLeft: 8 }} /> : (
                      activeSupplies.length === 0 ? (
                        <Empty
                          image={Empty.PRESENTED_IMAGE_SIMPLE}
                          description="Ingen aktive supplies — kør først et indgående scenarie"
                          style={{ marginTop: 12 }}
                        />
                      ) : (
                        <Select
                          value={selectedSupplyId}
                          onChange={setSelectedSupplyId}
                          disabled={running}
                          style={{ width: '100%', marginTop: 4 }}
                          options={activeSupplies.map(l => ({
                            value: l.id,
                            label: (
                              <Space>
                                <Text strong>{l.customerName}</Text>
                                <Text type="secondary" style={{ fontFamily: 'monospace', fontSize: 11 }}>
                                  {l.gsrn}
                                </Text>
                              </Space>
                            ),
                          }))}
                        />
                      )
                    )}
                  </div>
                  <div>
                    <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                      {scenario === 'supplier-out' ? 'Overtagelsesdato' : 'Fraflytningsdato'}
                    </Text>
                    <DatePicker
                      value={effectiveDate}
                      onChange={setEffectiveDate}
                      format="DD-MM-YYYY"
                      style={{ width: '100%', marginTop: 4 }}
                      disabled={running}
                    />
                  </div>
                </>
              ) : (
                /* Incoming: customer details form */
                <>
                  <div>
                    <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                      Customer
                    </Text>
                    <Input
                      value={customerName} onChange={e => setCustomerName(e.target.value)}
                      placeholder="Customernavn" disabled={running}
                      prefix={<UserOutlined style={{ color: '#99afc2' }} />}
                      style={{ marginTop: 4 }}
                    />
                  </div>

                  <Row gutter={12}>
                    <Col span={12}>
                      <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                        CPR
                      </Text>
                      <Input
                        value={cpr} onChange={e => setCpr(e.target.value)}
                        placeholder="DDMMYYXXXX" disabled={running}
                        style={{ marginTop: 4, fontFamily: 'monospace' }}
                      />
                    </Col>
                    <Col span={12}>
                      <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                        Effektiv dato
                      </Text>
                      <DatePicker
                        value={effectiveDate}
                        onChange={setEffectiveDate}
                        format="DD-MM-YYYY"
                        style={{ width: '100%', marginTop: 4 }}
                        disabled={running}
                      />
                    </Col>
                  </Row>

                  <div>
                    <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                      GSRN
                    </Text>
                    <Input
                      value={gsrn} onChange={e => setGsrn(e.target.value)}
                      placeholder="18-cifret GSRN" disabled={running}
                      style={{ marginTop: 4, fontFamily: 'monospace' }}
                    />
                  </div>

                  <Collapse
                    ghost size="small"
                    items={[{
                      key: 'address',
                      label: <Text type="secondary" style={{ fontSize: 12 }}>Adresse</Text>,
                      children: (
                        <Space direction="vertical" size={8} style={{ width: '100%' }}>
                          <Row gutter={8}>
                            <Col span={16}>
                              <Input value={street} onChange={e => setStreet(e.target.value)}
                                placeholder="Vejnavn" disabled={running} size="small" />
                            </Col>
                            <Col span={8}>
                              <Input value={buildingNumber} onChange={e => setBuildingNumber(e.target.value)}
                                placeholder="Nr." disabled={running} size="small" />
                            </Col>
                          </Row>
                          <Row gutter={8}>
                            <Col span={8}>
                              <Input value={postCode} onChange={e => setPostCode(e.target.value)}
                                placeholder="Postnr." disabled={running} size="small" />
                            </Col>
                            <Col span={16}>
                              <Input value={city} onChange={e => setCity(e.target.value)}
                                placeholder="By" disabled={running} size="small" />
                            </Col>
                          </Row>
                        </Space>
                      ),
                    }]}
                  />

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Text type="secondary" style={{ fontSize: 12 }}>Generer forbrugsdata (1 måned)</Text>
                    <Switch checked={generateConsumption} onChange={setGenerateConsumption} disabled={running} />
                  </div>
                </>
              )}

              <Divider style={{ margin: '8px 0' }} />

              {!running && !allDone && (
                <Button
                  type="primary" block size="large"
                  icon={<PlayCircleOutlined />}
                  onClick={runSimulation}
                  disabled={!canRun}
                  style={{
                    height: 48, fontWeight: 600, fontSize: 15,
                    background: `linear-gradient(135deg, ${config.color} 0%, ${config.color}dd 100%)`,
                    borderColor: 'transparent',
                  }}
                >
                  Kør {config.label.toLowerCase()}
                </Button>
              )}

              {allDone && (
                <Button block size="large" icon={<ReloadOutlined />} onClick={reset}
                  style={{ height: 48, fontWeight: 600, fontSize: 15 }}>
                  Ny simulation
                </Button>
              )}

              {running && (
                <Button block size="large" danger onClick={() => { abortRef.current = true; }}>
                  Afbryd
                </Button>
              )}
            </Space>
          </Card>
        </Col>

        {/* Right: Live process feed */}
        <Col xs={24} lg={14}>
          {steps.length === 0 ? (
            <Card style={{ borderRadius: 12, textAlign: 'center', padding: '60px 0' }}>
              <ExperimentOutlined style={{ fontSize: 48, color: '#c9d4de', marginBottom: 16 }} />
              <Title level={4} type="secondary">Klar til simulation</Title>
              <Paragraph type="secondary" style={{ maxWidth: 360, margin: '0 auto' }}>
                Vælg et scenarie, konfigurer parametrene, og tryk Kør.
                Systemet gennemfører den komplette forretningsproces
                og beregner automatisk settlement.
              </Paragraph>
            </Card>
          ) : (
            <Space direction="vertical" size={16} style={{ width: '100%' }}>
              <Card
                title="Procesforløb"
                extra={running && <Spin indicator={<LoadingOutlined spin />} />}
                style={{ borderRadius: 12 }}
              >
                <Steps
                  direction="vertical"
                  size="small"
                  current={currentStepIndex >= 0 ? currentStepIndex : (allDone ? steps.length : 0)}
                  items={steps.map(step => ({
                    title: (
                      <Space>
                        <span style={{ fontWeight: 600 }}>{step.title}</span>
                        {step.timestamp && (
                          <Text type="secondary" style={{ fontSize: 11, fontFamily: 'monospace' }}>
                            {step.timestamp}
                          </Text>
                        )}
                      </Space>
                    ),
                    description: (
                      <div>
                        <Text type="secondary" style={{ fontSize: 12 }}>{step.description}</Text>
                        {step.detail && (
                          <div style={{ marginTop: 2 }}>
                            <Text style={{ fontSize: 13 }}>
                              {step.status === 'running' && <LoadingOutlined style={{ marginRight: 6 }} />}
                              {step.detail}
                            </Text>
                          </div>
                        )}
                      </div>
                    ),
                    status: step.status === 'done' ? 'finish'
                      : step.status === 'running' ? 'process'
                      : step.status === 'error' ? 'error'
                      : 'wait',
                    icon: step.status === 'running' ? <LoadingOutlined /> : undefined,
                  }))}
                />
              </Card>

              {/* Result card */}
              {allDone && result && (
                <Card
                  style={{
                    borderRadius: 12,
                    background: config.needsExistingSupply
                      ? 'linear-gradient(135deg, #fef3c7 0%, #fde68a 100%)'
                      : 'linear-gradient(135deg, #f5f3ff 0%, #ede9fe 100%)',
                    border: config.needsExistingSupply ? '1px solid #fbbf24' : '1px solid #ddd6fe',
                  }}
                >
                  <Result
                    status="success"
                    title={result.message.split('.')[0]}
                    subTitle={result.message}
                    style={{ padding: '16px 0' }}
                    extra={
                      <Space wrap>
                        <Button
                          type="primary" icon={<UserOutlined />}
                          onClick={() => navigate(`/customers/${result.customerId}`)}
                        >
                          Se customer
                        </Button>
                        {settlement && (
                          <Button icon={<CalculatorOutlined />} onClick={() => navigate('/settlements')}>
                            Se settlement
                          </Button>
                        )}
                        <Button icon={<SwapOutlined />} onClick={() => navigate('/processes')}>
                          Se processer
                        </Button>
                      </Space>
                    }
                  />

                  <Divider style={{ margin: '8px 0 16px' }} />

                  <Descriptions size="small" column={{ xs: 1, sm: 2 }} bordered>
                    <Descriptions.Item label="Customer">{result.customerName}</Descriptions.Item>
                    <Descriptions.Item label="GSRN">
                      <Text copyable style={{ fontFamily: 'monospace', fontSize: 12 }}>{result.gsrn}</Text>
                    </Descriptions.Item>
                    <Descriptions.Item label="Effektiv dato">
                      {dayjs(result.effectiveDate).format('D. MMMM YYYY')}
                    </Descriptions.Item>
                    <Descriptions.Item label="Transaction ID">
                      <Text style={{ fontFamily: 'monospace', fontSize: 11 }}>{result.transactionId}</Text>
                    </Descriptions.Item>
                    <Descriptions.Item label="Status">
                      <Tag color="green">{result.status}</Tag>
                    </Descriptions.Item>
                    <Descriptions.Item label="Scenarie">
                      <Tag color={config.color} style={{ color: '#fff' }}>{config.label}</Tag>
                    </Descriptions.Item>
                    {result.totalEnergyKwh && (
                      <Descriptions.Item label="Forbrug">
                        {result.totalEnergyKwh.toFixed(1)} kWh
                      </Descriptions.Item>
                    )}
                  </Descriptions>

                  {/* Settlement Calculation Breakdown */}
                  {settlement && settlement.found && (
                    <Card
                      size="small"
                      style={{ marginTop: 16, background: '#f0fdf4', border: '1px solid #bbf7d0' }}
                      title={
                        <Space>
                          <CalculatorOutlined style={{ color: '#16a34a' }} />
                          <Text strong>Settlement Engine Result</Text>
                          <Tag color="green">Calculated</Tag>
                          <Text type="secondary" style={{ fontSize: 12 }}>
                            {new Date(settlement.calculatedAt).toLocaleTimeString('da-DK')}
                          </Text>
                        </Space>
                      }
                    >
                      <Descriptions size="small" column={{ xs: 1, sm: 3 }} bordered style={{ marginBottom: 12 }}>
                        <Descriptions.Item label="Period">
                          {dayjs(settlement.periodStart).format('D. MMM YYYY')} — {dayjs(settlement.periodEnd).format('D. MMM YYYY')}
                        </Descriptions.Item>
                        <Descriptions.Item label="Total Energy">
                          <Text strong className="tnum">{settlement.totalEnergyKwh.toFixed(1)} kWh</Text>
                        </Descriptions.Item>
                        <Descriptions.Item label="Total Amount">
                          <Text strong className="tnum" style={{ color: '#16a34a', fontSize: 16 }}>
                            {settlement.totalAmount.toLocaleString('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {settlement.currency}
                          </Text>
                        </Descriptions.Item>
                      </Descriptions>

                      {settlement.lines.length > 0 && (
                        <div>
                          <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 1 }}>
                            Calculation Breakdown
                          </Text>
                          <table style={{ width: '100%', marginTop: 8, fontSize: 13, borderCollapse: 'collapse' }}>
                            <thead>
                              <tr style={{ borderBottom: '1px solid #d1fae5', textAlign: 'left' }}>
                                <th style={{ padding: '6px 8px', color: '#6b7280', fontWeight: 500, fontSize: 11, textTransform: 'uppercase', letterSpacing: 1 }}>Charge</th>
                                <th style={{ padding: '6px 8px', color: '#6b7280', fontWeight: 500, fontSize: 11, textTransform: 'uppercase', letterSpacing: 1, textAlign: 'right' }}>Quantity</th>
                                <th style={{ padding: '6px 8px', color: '#6b7280', fontWeight: 500, fontSize: 11, textTransform: 'uppercase', letterSpacing: 1, textAlign: 'right' }}>Unit Price</th>
                                <th style={{ padding: '6px 8px', color: '#6b7280', fontWeight: 500, fontSize: 11, textTransform: 'uppercase', letterSpacing: 1, textAlign: 'right' }}>Amount</th>
                              </tr>
                            </thead>
                            <tbody>
                              {settlement.lines.map((line, i) => (
                                <tr key={i} style={{ borderBottom: '1px solid #ecfdf5' }}>
                                  <td style={{ padding: '8px', fontWeight: 500 }}>{line.description}</td>
                                  <td style={{ padding: '8px', textAlign: 'right', fontFeatureSettings: "'tnum'" }}>
                                    {line.quantityKwh.toFixed(2)} kWh
                                  </td>
                                  <td style={{ padding: '8px', textAlign: 'right', fontFeatureSettings: "'tnum'" }}>
                                    {line.unitPrice.toFixed(4)} {line.currency}/kWh
                                  </td>
                                  <td style={{ padding: '8px', textAlign: 'right', fontWeight: 600, fontFeatureSettings: "'tnum'" }}>
                                    {line.amount.toLocaleString('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {line.currency}
                                  </td>
                                </tr>
                              ))}
                              <tr style={{ borderTop: '2px solid #86efac' }}>
                                <td colSpan={3} style={{ padding: '8px', fontWeight: 700 }}>Total</td>
                                <td style={{ padding: '8px', textAlign: 'right', fontWeight: 700, fontSize: 15, color: '#16a34a', fontFeatureSettings: "'tnum'" }}>
                                  {settlement.totalAmount.toLocaleString('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {settlement.currency}
                                </td>
                              </tr>
                            </tbody>
                          </table>
                        </div>
                      )}
                    </Card>
                  )}
                </Card>
              )}

              {error && (
                <Alert type="error" showIcon message="Simulation errorede" description={error} />
              )}
            </Space>
          )}
        </Col>
      </Row>
    </Space>
  );
}
