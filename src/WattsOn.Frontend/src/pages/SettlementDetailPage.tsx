import { useEffect, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography,
  Button, Row, Col, Divider, Input, Modal, message, Statistic, Tooltip,
} from 'antd';
import {
  ArrowLeftOutlined, FileTextOutlined, SwapOutlined,
  CheckCircleOutlined, HomeOutlined, LinkOutlined,
  ExclamationCircleOutlined, CalculatorOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons';
import type { SettlementDocument, SettlementDocumentLine, RecalcResult, RecalcLine } from '../api/client';
import { getSettlementDocument, confirmSettlement, recalculateSettlement } from '../api/client';

const { Title, Text } = Typography;

import { formatDate, formatDateTime, formatPeriodEnd, formatDKK } from '../utils/format';

const docTypeConfig: Record<string, { label: string; bg: string; text: string; border: string; icon: React.ReactNode }> = {
  settlement: { label: 'Afregning', bg: '#e0e7ec', text: '#3d5468', border: '#b0c4d4', icon: <FileTextOutlined /> },
  debitNote: { label: 'Debitnota', bg: '#fed7aa', text: '#9a3412', border: '#fdba74', icon: <SwapOutlined /> },
  creditNote: { label: 'Kreditnota', bg: '#d1fae5', text: '#065f46', border: '#6ee7b7', icon: <SwapOutlined /> },
};

const statusConfig: Record<string, { label: string; bg: string; text: string; border: string }> = {
  Calculated: { label: 'Beregnet', bg: '#d1fae5', text: '#065f46', border: '#6ee7b7' },
  Invoiced: { label: 'Faktureret', bg: '#dbeafe', text: '#1e40af', border: '#93c5fd' },
  Adjusted: { label: 'Justeret', bg: '#fed7aa', text: '#9a3412', border: '#fdba74' },
  Migrated: { label: 'Migreret', bg: '#ddd6fe', text: '#5b21b6', border: '#a78bfa' },
};

export default function SettlementDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [doc, setDoc] = useState<SettlementDocument | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmModal, setConfirmModal] = useState(false);
  const [invoiceRef, setInvoiceRef] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [recalc, setRecalc] = useState<RecalcResult | null>(null);
  const [recalcLoading, setRecalcLoading] = useState(false);
  const [recalcOpen, setRecalcOpen] = useState(false);
  const recalcRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  const loadDoc = () => {
    if (!id) return;
    setLoading(true);
    getSettlementDocument(id)
      .then(res => setDoc(res.data))
      .catch(err => setError(err.response?.status === 404 ? 'Settlement ikke fundet' : err.message))
      .finally(() => setLoading(false));
  };

  useEffect(loadDoc, [id]);

  // Auto-scroll to comparison panel when results load or when panel opens (loading state)
  useEffect(() => {
    if (recalcOpen && recalcRef.current) {
      // Small delay to let the DOM render the card before scrolling
      requestAnimationFrame(() => {
        recalcRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      });
    }
  }, [recalcOpen, recalc]);

  const handleConfirm = async () => {
    if (!id || !invoiceRef.trim()) return;
    setConfirming(true);
    try {
      await confirmSettlement(id, invoiceRef.trim());
      message.success('Fakturering bekræftet');
      setConfirmModal(false);
      setInvoiceRef('');
      loadDoc();
    } catch (err: any) {
      message.error(err.response?.data || 'Kunne ikke bekræfte');
    } finally {
      setConfirming(false);
    }
  };

  const handleRecalculate = async () => {
    if (!doc) return;
    setRecalcLoading(true);
    setRecalcOpen(true);
    try {
      const res = await recalculateSettlement(doc.settlementId);
      setRecalc(res.data);
    } catch (err: any) {
      message.error('Genberegning fejlede: ' + (err.response?.data?.error || err.message));
    } finally {
      setRecalcLoading(false);
    }
  };

  // Build comparison table data: match original lines to recalculated by normalized description
  const buildComparisonRows = (result: RecalcResult) => {
    // Normalize migrated descriptions to match calculator output:
    // "Abon-Net [22000] (migreret, abonnement)" → "Abon-Net"
    // "Product Margin (Grøn strøm) [product=V Variabel] (migreret)" → "Grøn strøm"
    // "Leverandørmargin (migreret)" → "Leverandørmargin"
    const normalize = (d: string) => {
      let n = d.replace(/ \(migreret.*?\)/g, '').trim();  // strip (migreret...)
      n = n.replace(/ \[.*?\]/g, '').trim();               // strip [chargeId] / [product=...]
      // "Product Margin (X)" → "X"
      const pmMatch = n.match(/^Product Margin \((.+)\)$/);
      if (pmMatch) n = pmMatch[1];
      return n;
    };

    const origMap = new Map<string, RecalcLine>();
    const origDescMap = new Map<string, string>(); // normalized → original description
    for (const l of result.original.lines) {
      const key = normalize(l.description);
      origMap.set(key, l);
      origDescMap.set(key, l.description);
    }

    const recalcMap = new Map<string, RecalcLine>();
    if (result.recalculated) {
      for (const l of result.recalculated.lines) recalcMap.set(l.description, l);
    }

    // Try to match "Leverandørmargin" to base product margin if no direct match
    if (origMap.has('Leverandørmargin') && !recalcMap.has('Leverandørmargin')) {
      // Find the base product margin in recalc (not Grøn strøm or other addons)
      const baseMargin = result.recalculated?.lines.find(l =>
        l.source === 'SupplierMargin' && !origMap.has(l.description) && l.description !== 'Grøn strøm'
      );
      if (baseMargin) {
        // Rename the original entry to match the recalc name
        const origLine = origMap.get('Leverandørmargin')!;
        origMap.delete('Leverandørmargin');
        origMap.set(baseMargin.description, origLine);
        origDescMap.set(baseMargin.description, origDescMap.get('Leverandørmargin')!);
        origDescMap.delete('Leverandørmargin');
      }
    }

    const allKeys = new Set([...origMap.keys(), ...recalcMap.keys()]);
    return Array.from(allKeys).sort().map(key => {
      const orig = origMap.get(key);
      const recalc = recalcMap.get(key);
      const origAmt = orig?.amount ?? 0;
      const recalcAmt = recalc?.amount ?? 0;
      const diff = recalcAmt - origAmt;
      return {
        key,
        description: key,
        origDesc: origDescMap.get(key),
        origQty: orig?.quantityKwh ?? null,
        origUnit: orig?.unitPrice ?? null,
        origAmt,
        recalcQty: recalc?.quantityKwh ?? null,
        recalcUnit: recalc?.unitPrice ?? null,
        recalcAmt,
        diff,
        onlyOrig: !recalc && !!orig,
        onlyRecalc: !orig && !!recalc,
      };
    });
  };

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!doc) return null;

  const config = docTypeConfig[doc.documentType] || docTypeConfig.settlement;
  const canConfirm = doc.status === 'Calculated';

  const lineColumns = [
    { title: '#', dataIndex: 'lineNumber', key: 'lineNumber', width: 50 },
    { title: 'BESKRIVELSE', dataIndex: 'description', key: 'description' },
    {
      title: 'CHARGE ID', dataIndex: 'chargeId', key: 'chargeId',
      render: (v: string | null) => v ? <Text className="mono">{v}</Text> : '—',
    },
    {
      title: 'MÆNGDE', dataIndex: 'quantity', key: 'quantity', align: 'right' as const,
      render: (v: number, r: SettlementDocumentLine) => (
        <Text className="tnum">{v.toFixed(3)} {r.quantityUnit.toLowerCase()}</Text>
      ),
    },
    {
      title: 'ENHEDSPRIS', dataIndex: 'unitPrice', key: 'unitPrice', align: 'right' as const,
      render: (v: number) => <Text className="tnum">{v.toFixed(4)} DKK</Text>,
    },
    {
      title: 'BELØB', dataIndex: 'lineAmount', key: 'lineAmount', align: 'right' as const,
      render: (v: number) => (
        <Text strong className="tnum" style={{ color: v < 0 ? '#059669' : undefined }}>
          {formatDKK(v)}
        </Text>
      ),
    },
    {
      title: 'MOMS', key: 'tax', align: 'right' as const,
      render: (_: any, r: SettlementDocumentLine) => (
        <Text className="tnum">{formatDKK(r.taxAmount)}</Text>
      ),
    },
  ];

  return (
    <Space direction="vertical" size={20} style={{ width: '100%' }}>
      <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/settlements')}
        style={{ color: '#7593a9', fontWeight: 500, paddingLeft: 0 }}>
        ← Afregninger
      </Button>

      {/* Document header */}
      <Card style={{ borderRadius: 12 }}>
        <Row gutter={24} align="middle">
          <Col flex="auto">
            <Space size={12} align="center">
              <div style={{
                width: 48, height: 48, borderRadius: 12,
                background: `linear-gradient(135deg, ${config.bg}, ${config.border}40)`,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}>
                <span style={{ fontSize: 20, color: config.text }}>{config.icon}</span>
              </div>
              <div>
                <Title level={3} style={{ margin: 0 }}>{doc.documentId}</Title>
                <Space size={8} style={{ marginTop: 4 }}>
                  <Tag style={{ background: config.bg, color: config.text, border: `1px solid ${config.border}`, fontWeight: 500 }}>{config.label}</Tag>
                  {(() => {
                    const sc = statusConfig[doc.status];
                    return sc ? (
                      <Tag style={{ background: sc.bg, color: sc.text, border: `1px solid ${sc.border}`, fontWeight: 500 }}>
                        {sc.label}
                      </Tag>
                    ) : (
                      <Tag>{doc.status}</Tag>
                    );
                  })()}
                  {doc.originalDocumentId && (
                    <Text type="secondary">Korrigerer: {doc.originalDocumentId}</Text>
                  )}
                </Space>
              </div>
            </Space>
          </Col>
          <Col>
            <div style={{ textAlign: 'right' }}>
              <div className="micro-label">Total inkl. moms</div>
              <div className="amount amount-large" style={{
                marginTop: 4,
                color: doc.totalInclVat < 0 ? '#059669' : '#2d3a45',
              }}>
                {formatDKK(doc.totalInclVat)}
              </div>
            </div>
          </Col>
        </Row>

        {/* Action buttons */}
        <Divider style={{ margin: '20px 0 16px' }} />
        <Space>
          {canConfirm && (
            <Button
              type="primary" icon={<CheckCircleOutlined />}
              onClick={() => setConfirmModal(true)}
              style={{ background: '#059669', borderColor: '#059669' }}
            >
              Bekræft fakturering
            </Button>
          )}
          <Tooltip title="Genberegner afregningen med WattsOns beregningsmotor uden at gemme. Sammenligner med det originale resultat.">
            <Button
              icon={<CalculatorOutlined />}
              onClick={handleRecalculate}
              loading={recalcLoading}
              style={{ borderColor: '#5d7a91', color: '#3d5468', fontWeight: 500 }}
            >
              Genberegn & sammenlign
            </Button>
          </Tooltip>
        </Space>

        {doc.externalInvoiceReference && (
          <>
            <Divider style={{ margin: '20px 0 16px' }} />
            <Space>
              <CheckCircleOutlined style={{ color: '#059669' }} />
              <Text>Faktureret som <Text strong>{doc.externalInvoiceReference}</Text></Text>
              {doc.invoicedAt && <Text type="secondary">({formatDateTime(doc.invoicedAt)})</Text>}
            </Space>
          </>
        )}

        {/* Correction links */}
        {(doc.previousSettlementId || doc.adjustmentSettlementId) && (
          <>
            <Divider style={{ margin: '20px 0 16px' }} />
            <Space direction="vertical" size={8}>
              {doc.previousSettlementId && doc.originalDocumentId && (
                <Button
                  type="link"
                  icon={<LinkOutlined />}
                  onClick={() => navigate(`/settlements/${doc.previousSettlementId}`)}
                  style={{ padding: 0, height: 'auto', color: '#5d7a91' }}
                >
                  Original afregning: <Text strong style={{ color: '#5d7a91' }}>{doc.originalDocumentId}</Text>
                </Button>
              )}
              {doc.adjustmentSettlementId && doc.adjustmentDocumentId && (
                <Button
                  type="link"
                  icon={<ExclamationCircleOutlined />}
                  onClick={() => navigate(`/settlements/${doc.adjustmentSettlementId}`)}
                  style={{ padding: 0, height: 'auto', color: '#d97706' }}
                >
                  Korrektion oprettet: <Text strong style={{ color: '#d97706' }}>{doc.adjustmentDocumentId}</Text>
                </Button>
              )}
            </Space>
          </>
        )}
      </Card>

      {/* Parties + details */}
      <Row gutter={16}>
        <Col xs={24} md={8}>
          <Card title="Sælger" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Space direction="vertical" size={4}>
              <Text strong>{doc.seller.name}</Text>
              <Text type="secondary" style={{ fontSize: 12 }}>{doc.seller.identifierScheme}: {doc.seller.identifier}</Text>
              <Text className="mono">GLN {doc.seller.glnNumber}</Text>
            </Space>
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="Køber" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Space direction="vertical" size={4}>
              <Text strong>{doc.buyer.name}</Text>
              <Text type="secondary" style={{ fontSize: 12 }}>{doc.buyer.identifierScheme}: {doc.buyer.identifier}</Text>
              {doc.buyer.address && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  <HomeOutlined style={{ marginRight: 4 }} />
                  {doc.buyer.address.streetName} {doc.buyer.address.buildingNumber},
                  {' '}{doc.buyer.address.postCode} {doc.buyer.address.cityName}
                </Text>
              )}
            </Space>
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="Detaljer" size="small" style={{ borderRadius: 12, height: '100%' }}>
            <Descriptions size="small" column={1} colon={false}>
              <Descriptions.Item label="Periode">
                <Text className="tnum" style={{ fontSize: 12 }}>
                  {formatDate(doc.period.start)} — {doc.period.end ? formatPeriodEnd(doc.period.end) : '→'}
                </Text>
              </Descriptions.Item>
              <Descriptions.Item label="Målepunkt">
                <Text className="mono">{doc.meteringPoint.gsrn}</Text>
              </Descriptions.Item>
              <Descriptions.Item label="Prisområde">{doc.meteringPoint.gridArea}</Descriptions.Item>
              <Descriptions.Item label="Beregnet">
                <Text className="tnum" style={{ fontSize: 12 }}>{formatDateTime(doc.calculatedAt)}</Text>
              </Descriptions.Item>
            </Descriptions>
          </Card>
        </Col>
      </Row>

      {/* Line items */}
      <Card title="Linjer" style={{ borderRadius: 12 }}>
        <Table
          dataSource={doc.lines}
          columns={lineColumns}
          rowKey="lineNumber"
          pagination={false}
          size="small"
          summary={() => (
            <>
              <Table.Summary.Row>
                <Table.Summary.Cell index={0} colSpan={5} align="right">
                  <Text strong>Subtotal excl. moms</Text>
                </Table.Summary.Cell>
                <Table.Summary.Cell index={5} align="right">
                  <Text strong className="tnum">{formatDKK(doc.totalExclVat)}</Text>
                </Table.Summary.Cell>
                <Table.Summary.Cell index={6} />
              </Table.Summary.Row>
              {doc.taxSummary.map(tax => (
                <Table.Summary.Row key={`${tax.taxCategory}-${tax.taxPercent}`}>
                  <Table.Summary.Cell index={0} colSpan={5} align="right">
                    <Text type="secondary">
                      Moms af {formatDKK(tax.taxableAmount)}
                    </Text>
                  </Table.Summary.Cell>
                  <Table.Summary.Cell index={5} align="right">
                    <Text className="tnum">{formatDKK(tax.taxAmount)}</Text>
                  </Table.Summary.Cell>
                  <Table.Summary.Cell index={6} />
                </Table.Summary.Row>
              ))}
              <Table.Summary.Row style={{ background: '#f8fafb' }}>
                <Table.Summary.Cell index={0} colSpan={5} align="right">
                  <Text strong style={{ fontSize: 15 }}>Total inkl. moms</Text>
                </Table.Summary.Cell>
                <Table.Summary.Cell index={5} align="right">
                  <Text strong className="tnum" style={{
                    fontSize: 15, color: doc.totalInclVat < 0 ? '#059669' : undefined,
                  }}>
                    {formatDKK(doc.totalInclVat)}
                  </Text>
                </Table.Summary.Cell>
                <Table.Summary.Cell index={6} />
              </Table.Summary.Row>
            </>
          )}
        />
      </Card>

      {/* Recalculation comparison */}
      {recalcOpen && (
        <div ref={recalcRef}>
        <Card
          title={
            <Space>
              <CalculatorOutlined />
              <span>Genberegning — sammenligning</span>
              <Tag style={{ background: '#dbeafe', color: '#1e40af', border: '1px solid #93c5fd' }}>Ikke gemt</Tag>
            </Space>
          }
          extra={<Button size="small" onClick={() => { setRecalcOpen(false); setRecalc(null); }}>Luk</Button>}
          style={{ borderRadius: 12 }}
        >
          {recalcLoading && <Spin style={{ display: 'block', margin: '40px auto' }} />}
          {recalc && !recalcLoading && (
            <Space direction="vertical" size={16} style={{ width: '100%' }}>
              {/* Summary stats */}
              <Row gutter={16}>
                <Col xs={12} md={6}>
                  <Statistic title="Observationer" value={recalc.observationsInPeriod} />
                </Col>
                <Col xs={12} md={6}>
                  <Statistic title="Spotpriser" value={recalc.spotPricesInPeriod} />
                </Col>
                <Col xs={12} md={6}>
                  <Statistic title="Pristilknytninger" value={recalc.datahubPriceLinks} />
                </Col>
                <Col xs={12} md={6}>
                  <Statistic
                    title="Prismodel"
                    value={recalc.pricingModel === 'SpotAddon' ? 'Spot + tillæg' : recalc.pricingModel}
                    valueStyle={{ fontSize: 16 }}
                  />
                </Col>
              </Row>

              {recalc.recalcError && !recalc.recalculated && (
                <Alert type="warning" showIcon message="Genberegning kunne ikke gennemføres" description={recalc.recalcError} />
              )}

              {/* Totals comparison */}
              {recalc.recalculated && (
                <>
                  <Row gutter={16}>
                    <Col xs={24} md={8}>
                      <Card size="small" style={{ borderRadius: 10, background: '#f8fafb' }}>
                        <div className="micro-label">ORIGINAL</div>
                        <div className="amount" style={{ fontSize: 22, marginTop: 4 }}>
                          {formatDKK(recalc.original.totalAmount)}
                        </div>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {recalc.original.totalEnergyKwh.toFixed(2)} kWh
                        </Text>
                      </Card>
                    </Col>
                    <Col xs={24} md={8}>
                      <Card size="small" style={{ borderRadius: 10, background: '#f0fdf4' }}>
                        <div className="micro-label">GENBEREGNET</div>
                        <div className="amount" style={{ fontSize: 22, marginTop: 4 }}>
                          {formatDKK(recalc.recalculated.totalAmount)}
                        </div>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {recalc.recalculated.totalEnergyKwh.toFixed(2)} kWh
                        </Text>
                      </Card>
                    </Col>
                    <Col xs={24} md={8}>
                      <Card size="small" style={{
                        borderRadius: 10,
                        background: recalc.comparison && Math.abs(recalc.comparison.totalAmountDiff) < 1 ? '#f0fdf4' : '#fefce8',
                      }}>
                        <div className="micro-label">DIFFERENCE</div>
                        <div className="amount" style={{
                          fontSize: 22, marginTop: 4,
                          color: recalc.comparison && Math.abs(recalc.comparison.totalAmountDiff) < 1 ? '#059669'
                            : recalc.comparison && Math.abs(recalc.comparison.totalAmountDiff) < 50 ? '#d97706' : '#dc2626',
                        }}>
                          {recalc.comparison ? (recalc.comparison.totalAmountDiff >= 0 ? '+' : '') + formatDKK(recalc.comparison.totalAmountDiff) : '—'}
                        </div>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {recalc.comparison && recalc.original.totalAmount !== 0
                            ? `${((recalc.comparison.totalAmountDiff / recalc.original.totalAmount) * 100).toFixed(1)}%`
                            : ''}
                        </Text>
                      </Card>
                    </Col>
                  </Row>

                  {/* Line-by-line comparison table */}
                  <Table
                    dataSource={buildComparisonRows(recalc)}
                    rowKey="key"
                    pagination={false}
                    size="small"
                    rowClassName={r => r.onlyOrig ? 'row-missing-recalc' : r.onlyRecalc ? 'row-extra-recalc' : ''}
                    columns={[
                      {
                        title: 'LINJE', dataIndex: 'description', key: 'description',
                        render: (v: string, r: any) => (
                          <Space size={4}>
                            <Text style={{ fontSize: 12 }}>{v}</Text>
                            {r.onlyOrig && <Tag color="#8c8c8c" style={{ color: '#fff', fontSize: 11 }}>kun original</Tag>}
                            {r.onlyRecalc && <Tag color="#2563eb" style={{ color: '#fff', fontSize: 11 }}>kun genberegnet</Tag>}
                          </Space>
                        ),
                      },
                      {
                        title: 'ORIGINAL', key: 'orig', align: 'right' as const, width: 120,
                        render: (_: any, r: any) => r.origAmt !== 0 ? (
                          <Text className="tnum" style={{ fontSize: 12 }}>{formatDKK(r.origAmt)}</Text>
                        ) : <Text type="secondary">—</Text>,
                      },
                      {
                        title: 'GENBEREGNET', key: 'recalc', align: 'right' as const, width: 120,
                        render: (_: any, r: any) => r.recalcAmt !== 0 ? (
                          <Text className="tnum" style={{ fontSize: 12 }}>{formatDKK(r.recalcAmt)}</Text>
                        ) : <Text type="secondary">—</Text>,
                      },
                      {
                        title: 'DIFF', key: 'diff', align: 'right' as const, width: 100,
                        render: (_: any, r: any) => {
                          if (Math.abs(r.diff) < 0.01) return <Text type="success" style={{ fontSize: 12 }}>✓</Text>;
                          const color = Math.abs(r.diff) < 1 ? '#059669' : Math.abs(r.diff) < 10 ? '#d97706' : '#dc2626';
                          return <Text className="tnum" style={{ fontSize: 12, color }}>{(r.diff >= 0 ? '+' : '') + formatDKK(r.diff)}</Text>;
                        },
                      },
                    ]}
                    summary={() => {
                      const totalOrig = recalc.original.totalAmount;
                      const totalRecalc = recalc.recalculated!.totalAmount;
                      const totalDiff = totalRecalc - totalOrig;
                      return (
                        <Table.Summary.Row style={{ background: '#f8fafb' }}>
                          <Table.Summary.Cell index={0}><Text strong>Total</Text></Table.Summary.Cell>
                          <Table.Summary.Cell index={1} align="right">
                            <Text strong className="tnum">{formatDKK(totalOrig)}</Text>
                          </Table.Summary.Cell>
                          <Table.Summary.Cell index={2} align="right">
                            <Text strong className="tnum">{formatDKK(totalRecalc)}</Text>
                          </Table.Summary.Cell>
                          <Table.Summary.Cell index={3} align="right">
                            <Text strong className="tnum" style={{
                              color: Math.abs(totalDiff) < 1 ? '#059669' : Math.abs(totalDiff) < 50 ? '#d97706' : '#dc2626',
                            }}>
                              {(totalDiff >= 0 ? '+' : '') + formatDKK(totalDiff)}
                            </Text>
                          </Table.Summary.Cell>
                        </Table.Summary.Row>
                      );
                    }}
                  />

                  <Alert
                    type="info" showIcon icon={<InfoCircleOutlined />}
                    message="Genberegningen er ikke gemt"
                    description="Denne genberegning bruger WattsOns beregningsmotor med aktuelle priser og tidsserier. Resultatet gemmes ikke — det er kun til sammenligning."
                    style={{ borderRadius: 8 }}
                  />
                </>
              )}

              {/* No observations state */}
              {!recalc.recalculated && recalc.recalcError && (
                <Alert
                  type="info" showIcon
                  message="Manglende data"
                  description={
                    <Space direction="vertical">
                      <Text>{recalc.recalcError}</Text>
                      <Text type="secondary">
                        Genberegning kræver tidsserier (forbrugsdata) for perioden. Ældre migrerede
                        perioder har muligvis ikke tidsserier i WattsOn endnu.
                      </Text>
                    </Space>
                  }
                  style={{ borderRadius: 8 }}
                />
              )}
            </Space>
          )}
        </Card>
        </div>
      )}

      {/* Confirm modal */}
      <Modal
        title="Bekræft fakturering"
        open={confirmModal}
        onCancel={() => setConfirmModal(false)}
        onOk={handleConfirm}
        confirmLoading={confirming}
        okText="Bekræft"
        okButtonProps={{ disabled: !invoiceRef.trim(), style: { background: '#059669', borderColor: '#059669' } }}
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Text>
            Bekræft at <Text strong>{doc.documentId}</Text> ({formatDKK(doc.totalInclVat)})
            er faktureret i det eksterne system.
          </Text>
          <div>
            <div className="micro-label" style={{ marginBottom: 4 }}>Ekstern fakturareference</div>
            <Input
              placeholder="f.eks. INV-2026-0042"
              value={invoiceRef}
              onChange={e => setInvoiceRef(e.target.value)}
              onPressEnter={handleConfirm}
              autoFocus
            />
          </div>
        </Space>
      </Modal>
    </Space>
  );
}
