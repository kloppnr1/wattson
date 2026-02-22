import { useEffect, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Collapse, Descriptions, Table, Tag, Spin, Alert, Space, Typography,
  Button, Row, Col, Divider, Input, Modal, message, Statistic, Tooltip,
} from 'antd';
import {
  ArrowLeftOutlined, FileTextOutlined, SwapOutlined,
  CheckCircleOutlined, HomeOutlined, LinkOutlined,
  ExclamationCircleOutlined, CalculatorOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons';
import type { SettlementDocument, SettlementDocumentLine, RecalcResult, RecalcLine, LineDetails, DailyDetail } from '../api/client';
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
  const [expandedCompRows, setExpandedCompRows] = useState<Set<string>>(new Set());
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

    // Classify lines into categories for grouping
    const classifyLine = (desc: string, source: string): string => {
      if (source === 'SpotPrice') return 'spot';
      if (source === 'SupplierMargin') return 'margin';
      // DataHub charges
      const d = desc.toLowerCase();
      if (d.includes('abo') || d.includes('abonnement')) return 'subscription';
      return 'tariff';
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
      const baseMargin = result.recalculated?.lines.find(l =>
        l.source === 'SupplierMargin' && !origMap.has(l.description) && l.description !== 'Grøn strøm'
      );
      if (baseMargin) {
        const origLine = origMap.get('Leverandørmargin')!;
        origMap.delete('Leverandørmargin');
        origMap.set(baseMargin.description, origLine);
        origDescMap.set(baseMargin.description, origDescMap.get('Leverandørmargin')!);
        origDescMap.delete('Leverandørmargin');
      }
    }

    const allKeys = new Set([...origMap.keys(), ...recalcMap.keys()]);
    const rows = Array.from(allKeys).map(key => {
      const orig = origMap.get(key);
      const recalc = recalcMap.get(key);
      const origAmt = orig?.amount ?? 0;
      const recalcAmt = recalc?.amount ?? 0;
      const diff = recalcAmt - origAmt;
      const source = recalc?.source ?? orig?.source ?? '';
      const category = classifyLine(key, source);
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
        diffPct: origAmt !== 0 ? (diff / Math.abs(origAmt)) * 100 : null,
        onlyOrig: !recalc && !!orig,
        onlyRecalc: !orig && !!recalc,
        details: recalc?.details ?? null,
        category,
      };
    })
    // Filter out noise: both sides zero or negligible
    .filter(r => Math.abs(r.origAmt) >= 0.01 || Math.abs(r.recalcAmt) >= 0.01);

    // Sort by category, then by amount descending
    const categoryOrder: Record<string, number> = { tariff: 0, subscription: 1, spot: 2, margin: 3 };
    rows.sort((a, b) => {
      const catDiff = (categoryOrder[a.category] ?? 9) - (categoryOrder[b.category] ?? 9);
      if (catDiff !== 0) return catDiff;
      return Math.abs(b.origAmt || b.recalcAmt) - Math.abs(a.origAmt || a.recalcAmt);
    });

    return rows;
  };

  // Category labels and colors for grouped display
  const categoryConfig: Record<string, { label: string; color: string; bg: string }> = {
    tariff: { label: 'Tariffer', color: '#1e40af', bg: '#dbeafe' },
    subscription: { label: 'Abonnementer', color: '#7c3aed', bg: '#ede9fe' },
    spot: { label: 'Spotpris', color: '#0369a1', bg: '#e0f2fe' },
    margin: { label: 'Leverandør', color: '#047857', bg: '#d1fae5' },
  };

  // Format rate for display
  const fmtRate = (v: number) => v < 0.01 && v > 0 ? v.toFixed(6) : v < 1 ? v.toFixed(4) : v.toFixed(2);

  // Shared daily/hourly breakdown table
  const renderDailyBreakdown = (daily: DailyDetail[]) => {
    if (!daily || daily.length === 0) return null;
    const totalHours = daily.reduce((s, d) => s + d.hours.length, 0);
    return (
      <Collapse
        size="small"
        style={{ marginTop: 8 }}
        items={[{
          key: 'daily',
          label: <span style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
            Dag- og timefordeling — {daily.length} dage, {totalHours} timer
          </span>,
          children: (
            <div style={{ maxHeight: 420, overflowY: 'auto', fontSize: 12 }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ borderBottom: '2px solid #e5e7eb', position: 'sticky', top: 0, background: '#fff', zIndex: 1 }}>
                    <th style={{ textAlign: 'left', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Dato / Time</th>
                    <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>kWh</th>
                    <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Sats</th>
                    <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Beløb</th>
                  </tr>
                </thead>
                <tbody>
                  {daily.map((day) => {
                    const dayLabel = new Date(day.date + 'T12:00:00').toLocaleDateString('da-DK', { day: 'numeric', month: 'short', year: 'numeric' });
                    return [
                      <tr key={`day-${day.date}`} style={{ background: '#f8fafb', borderTop: '1px solid #e5e7eb' }}>
                        <td style={{ padding: '5px 6px', fontWeight: 600, color: '#374151' }}>{dayLabel}</td>
                        <td className="tnum" style={{ padding: '5px 6px', textAlign: 'right', fontWeight: 600, color: '#374151' }}>{day.kwh.toFixed(2)}</td>
                        <td style={{ padding: '5px 6px' }}></td>
                        <td className="tnum" style={{ padding: '5px 6px', textAlign: 'right', fontWeight: 600, color: '#374151' }}>{formatDKK(day.amount)}</td>
                      </tr>,
                      ...day.hours.map((h) => (
                        <tr key={`${day.date}-${h.hour}`} style={{ borderBottom: '1px solid #f3f4f6' }}>
                          <td style={{ padding: '3px 6px 3px 20px', color: '#6b7280' }}>{String(h.hour).padStart(2, '0')}:00</td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right' }}>{h.kwh.toFixed(3)}</td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right', color: '#6b7280' }}>{fmtRate(h.rate)}</td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right' }}>{formatDKK(h.amount)}</td>
                        </tr>
                      )),
                    ];
                  })}
                </tbody>
              </table>
            </div>
          ),
        }]}
      />
    );
  };

  // Expandable row: per-line calculation breakdown
  const renderLineDetail = (row: ReturnType<typeof buildComparisonRows>[number]) => {
    const d = row.details;
    const hasOrig = row.origQty !== null;
    const hasRecalc = row.recalcQty !== null;

    return (
      <div style={{ padding: '8px 0' }}>
        {/* Side-by-side qty × rate breakdown */}
        <Row gutter={24}>
          {hasOrig && (
            <Col xs={24} md={12}>
              <div style={{ background: '#f8fafb', borderRadius: 8, padding: '10px 14px', marginBottom: 8 }}>
                <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 6 }}>Original</div>
                <div style={{ fontSize: 13 }}>
                  <span className="tnum" style={{ fontWeight: 500 }}>{row.origQty!.toFixed(2)}</span>
                  <Text type="secondary"> {d?.type === 'abonnement' ? 'dage' : 'kWh'} × </Text>
                  <span className="tnum" style={{ fontWeight: 500 }}>{fmtRate(row.origUnit!)}</span>
                  <Text type="secondary"> DKK/{d?.type === 'abonnement' ? 'dag' : 'kWh'}</Text>
                </div>
                <div style={{ marginTop: 2 }}>
                  <Text type="secondary" style={{ fontSize: 12 }}>= </Text>
                  <span className="tnum" style={{ fontWeight: 600, fontSize: 14 }}>{formatDKK(row.origAmt)}</span>
                </div>
              </div>
            </Col>
          )}
          {hasRecalc && (
            <Col xs={24} md={12}>
              <div style={{ background: '#f0fdf4', borderRadius: 8, padding: '10px 14px', marginBottom: 8 }}>
                <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 6 }}>Genberegnet</div>
                <div style={{ fontSize: 13 }}>
                  <span className="tnum" style={{ fontWeight: 500 }}>{row.recalcQty!.toFixed(2)}</span>
                  <Text type="secondary"> {d?.type === 'abonnement' ? 'dage' : 'kWh'} × </Text>
                  <span className="tnum" style={{ fontWeight: 500 }}>{fmtRate(row.recalcUnit!)}</span>
                  <Text type="secondary"> DKK/{d?.type === 'abonnement' ? 'dag' : 'kWh'}{d?.type === 'tarif' ? ' (vægtet gns.)' : ''}</Text>
                </div>
                <div style={{ marginTop: 2 }}>
                  <Text type="secondary" style={{ fontSize: 12 }}>= </Text>
                  <span className="tnum" style={{ fontWeight: 600, fontSize: 14 }}>{formatDKK(row.recalcAmt)}</span>
                </div>
              </div>
            </Col>
          )}
        </Row>

        {/* Tariff tier breakdown */}
        {d?.type === 'tarif' && d.tiers && d.tiers.length > 0 && (
          <div style={{ marginTop: 4 }}>
            <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 6 }}>
              Satsfordeling — {d.totalHours} timer, {d.hoursWithPrice} med pris
            </div>
            <table style={{ width: '100%', maxWidth: 480, fontSize: 12, borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid #e5e7eb' }}>
                  <th style={{ textAlign: 'left', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Sats</th>
                  <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Timer</th>
                  <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>kWh</th>
                  <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Beløb</th>
                </tr>
              </thead>
              <tbody>
                {d.tiers.map((t, i) => (
                  <tr key={i} style={{ borderBottom: '1px solid #f3f4f6' }}>
                    <td className="tnum" style={{ padding: '4px 8px' }}>{fmtRate(t.rate)} DKK/kWh</td>
                    <td className="tnum" style={{ padding: '4px 8px', textAlign: 'right' }}>{t.hours}</td>
                    <td className="tnum" style={{ padding: '4px 8px', textAlign: 'right' }}>{t.kwh.toFixed(2)}</td>
                    <td className="tnum" style={{ padding: '4px 8px', textAlign: 'right', fontWeight: 500 }}>{formatDKK(t.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Subscription detail */}
        {d?.type === 'abonnement' && (
          <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
            {d.days} dage × {fmtRate(d.dailyRate!)} DKK/dag = {formatDKK(d.days! * d.dailyRate!)}
          </div>
        )}

        {/* Spot price stats */}
        {d?.type === 'spot' && (
          <div style={{ marginTop: 4 }}>
            <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 4 }}>
              Spotprisstatistik
            </div>
            <div style={{ fontSize: 12, display: 'flex', gap: 16 }}>
              <span>
                <Text type="secondary">Timer: </Text>
                <span className="tnum">{d.hoursWithPrice}/{d.totalHours}</span>
                {d.hoursMissing! > 0 && <Text type="warning" style={{ marginLeft: 4 }}>({d.hoursMissing} mangler)</Text>}
              </span>
              <span><Text type="secondary">Gns: </Text><span className="tnum">{fmtRate(d.avgRate!)}</span></span>
              <span><Text type="secondary">Min: </Text><span className="tnum">{fmtRate(d.minRate!)}</span></span>
              <span><Text type="secondary">Maks: </Text><span className="tnum">{fmtRate(d.maxRate!)}</span></span>
              <Text type="secondary">DKK/kWh</Text>
            </div>
          </div>
        )}

        {/* Margin detail */}
        {d?.type === 'margin' && (
          <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
            {row.recalcQty?.toFixed(2)} kWh × {fmtRate(d.ratePerKwh!)} DKK/kWh = {formatDKK(row.recalcAmt)}
          </div>
        )}

        {/* Daily/hourly breakdown */}
        {d?.daily && renderDailyBreakdown(d.daily)}
      </div>
    );
  };

  // Expandable row for main settlement lines table
  const renderSettlementLineDetail = (line: SettlementDocumentLine) => {
    const d = line.details;
    const isSub = d?.type === 'abonnement';
    const unit = isSub ? 'dage' : 'kWh';
    const unitLabel = isSub ? 'dag' : 'kWh';
    // Migrated subscriptions: qty=0, unitPrice=total monthly amount
    const isMigratedSub = isSub && line.quantity === 0;

    return (
      <div style={{ padding: '8px 0' }}>
        {/* Qty × rate breakdown */}
        <div style={{ background: '#f8fafb', borderRadius: 8, padding: '10px 14px', marginBottom: 8, maxWidth: 480 }}>
          <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 6 }}>
            {d?.type === 'tarif' ? 'Tarif' : d?.type === 'abonnement' ? 'Abonnement' : d?.type === 'spot' ? 'Spotpris' : d?.type === 'margin' ? 'Leverandørmargin' : line.source}
            {line.chargeId && <span style={{ marginLeft: 8, fontWeight: 400, textTransform: 'none' }}>Charge: {line.chargeId}</span>}
            {line.chargeOwnerGln && <span style={{ marginLeft: 8, fontWeight: 400, textTransform: 'none' }}>GLN: {line.chargeOwnerGln}</span>}
          </div>
          {isMigratedSub ? (
            <div style={{ fontSize: 13 }}>
              <span className="tnum" style={{ fontWeight: 500 }}>{formatDKK(line.lineAmount)}</span>
              <Text type="secondary"> / måned</Text>
              {d?.dailyRate != null && d.dailyRate > 0 && (
                <Text type="secondary" style={{ marginLeft: 8, fontSize: 12 }}>
                  ({d.days?.toFixed(1)} dage × {fmtRate(d.dailyRate)} DKK/dag)
                </Text>
              )}
            </div>
          ) : (
            <>
              <div style={{ fontSize: 13 }}>
                <span className="tnum" style={{ fontWeight: 500 }}>{line.quantity.toFixed(2)}</span>
                <Text type="secondary"> {unit} × </Text>
                <span className="tnum" style={{ fontWeight: 500 }}>{fmtRate(line.unitPrice)}</span>
                <Text type="secondary"> DKK/{unitLabel}{d?.type === 'tarif' ? ' (vægtet gns.)' : ''}</Text>
              </div>
              <div style={{ marginTop: 2 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>= </Text>
                <span className="tnum" style={{ fontWeight: 600, fontSize: 14 }}>{formatDKK(line.lineAmount)}</span>
                {line.taxAmount !== 0 && (
                  <Text type="secondary" style={{ fontSize: 12, marginLeft: 8 }}>+ {formatDKK(line.taxAmount)} moms</Text>
                )}
              </div>
            </>
          )}
        </div>

        {/* Tariff tier breakdown */}
        {d?.type === 'tarif' && d.tiers && d.tiers.length > 0 && (
          <div style={{ marginTop: 4 }}>
            <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 6 }}>
              Satsfordeling — {d.totalHours} timer, {d.hoursWithPrice} med pris
            </div>
            <table style={{ width: '100%', maxWidth: 480, fontSize: 12, borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid #e5e7eb' }}>
                  <th style={{ textAlign: 'left', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Sats</th>
                  <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Timer</th>
                  <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>kWh</th>
                  <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Beløb</th>
                </tr>
              </thead>
              <tbody>
                {d.tiers.map((t, i) => (
                  <tr key={i} style={{ borderBottom: '1px solid #f3f4f6' }}>
                    <td className="tnum" style={{ padding: '4px 8px' }}>{fmtRate(t.rate)} DKK/kWh</td>
                    <td className="tnum" style={{ padding: '4px 8px', textAlign: 'right' }}>{t.hours}</td>
                    <td className="tnum" style={{ padding: '4px 8px', textAlign: 'right' }}>{t.kwh.toFixed(2)}</td>
                    <td className="tnum" style={{ padding: '4px 8px', textAlign: 'right', fontWeight: 500 }}>{formatDKK(t.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {d?.type === 'tarif' && d.totalHours === 0 && (
          <Text type="secondary" style={{ fontSize: 12 }}>Ingen observationer tilgængelige for satsfordeling</Text>
        )}

        {/* Subscription detail */}
        {d?.type === 'abonnement' && (
          <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
            {d.days?.toFixed(1)} dage × {fmtRate(d.dailyRate!)} DKK/dag = {formatDKK(d.days! * d.dailyRate!)}
          </div>
        )}

        {/* Spot price stats */}
        {d?.type === 'spot' && (
          <div style={{ marginTop: 4 }}>
            <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 4 }}>
              Spotprisstatistik
            </div>
            <div style={{ fontSize: 12, display: 'flex', gap: 16, flexWrap: 'wrap' }}>
              <span>
                <Text type="secondary">Timer: </Text>
                <span className="tnum">{d.hoursWithPrice}/{d.totalHours}</span>
                {d.hoursMissing! > 0 && <Text type="warning" style={{ marginLeft: 4 }}>({d.hoursMissing} mangler)</Text>}
              </span>
              <span><Text type="secondary">Gns: </Text><span className="tnum">{fmtRate(d.avgRate!)}</span></span>
              <span><Text type="secondary">Min: </Text><span className="tnum">{fmtRate(d.minRate!)}</span></span>
              <span><Text type="secondary">Maks: </Text><span className="tnum">{fmtRate(d.maxRate!)}</span></span>
              <Text type="secondary">DKK/kWh</Text>
            </div>
          </div>
        )}

        {/* Margin detail */}
        {d?.type === 'margin' && (
          <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
            {line.quantity.toFixed(2)} kWh × {fmtRate(d.ratePerKwh!)} DKK/kWh = {formatDKK(line.lineAmount)}
          </div>
        )}

        {/* Daily/hourly breakdown */}
        {d?.daily && renderDailyBreakdown(d.daily)}

        {/* No details available */}
        {!d && (
          <Text type="secondary" style={{ fontSize: 12 }}>Ingen yderligere detaljer tilgængelige</Text>
        )}
      </div>
    );
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
          expandable={{
            expandedRowRender: renderSettlementLineDetail,
            rowExpandable: () => true,
          }}
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
              {recalc.recalculated && (() => {
                const compRows = buildComparisonRows(recalc);
                const totalOrig = recalc.original.totalAmount;
                const totalRecalc = recalc.recalculated!.totalAmount;
                const totalDiff = totalRecalc - totalOrig;
                const totalPct = totalOrig !== 0 ? (totalDiff / Math.abs(totalOrig)) * 100 : 0;
                const energyDiff = recalc.recalculated!.totalEnergyKwh - recalc.original.totalEnergyKwh;
                const energyPct = recalc.original.totalEnergyKwh !== 0
                  ? (energyDiff / recalc.original.totalEnergyKwh) * 100 : 0;

                // Compute category subtotals
                const categories = ['tariff', 'subscription', 'spot', 'margin'] as const;
                const catSubtotals = categories.map(cat => {
                  const catRows = compRows.filter(r => r.category === cat);
                  if (catRows.length === 0) return null;
                  const origSum = catRows.reduce((s, r) => s + r.origAmt, 0);
                  const recalcSum = catRows.reduce((s, r) => s + r.recalcAmt, 0);
                  const diff = recalcSum - origSum;
                  return { cat, rows: catRows, origSum, recalcSum, diff, pct: origSum !== 0 ? (diff / Math.abs(origSum)) * 100 : null };
                }).filter(Boolean) as { cat: string; rows: typeof compRows; origSum: number; recalcSum: number; diff: number; pct: number | null }[];

                const diffColor = (d: number) => Math.abs(d) < 0.5 ? '#059669' : Math.abs(d) < 10 ? '#d97706' : '#dc2626';
                const diffPctBadge = (d: number, pct: number | null) => {
                  if (Math.abs(d) < 0.01) return <Text style={{ fontSize: 11, color: '#059669' }}>✓</Text>;
                  const c = diffColor(d);
                  return (
                    <span style={{ color: c, fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                      {(d >= 0 ? '+' : '') + formatDKK(d)}
                      {pct !== null && <span style={{ marginLeft: 4, fontSize: 11, opacity: 0.7 }}>({pct >= 0 ? '+' : ''}{pct.toFixed(1)}%)</span>}
                    </span>
                  );
                };

                return (
                <>
                  {/* Top summary: amount + energy comparison */}
                  <Row gutter={16}>
                    <Col xs={12} md={6}>
                      <Card size="small" style={{ borderRadius: 10, background: '#f8fafb' }}>
                        <div className="micro-label">ORIGINAL</div>
                        <div className="amount" style={{ fontSize: 20, marginTop: 4 }}>{formatDKK(totalOrig)}</div>
                        <Text type="secondary" style={{ fontSize: 12 }}>{recalc.original.totalEnergyKwh.toFixed(2)} kWh</Text>
                      </Card>
                    </Col>
                    <Col xs={12} md={6}>
                      <Card size="small" style={{ borderRadius: 10, background: '#f0fdf4' }}>
                        <div className="micro-label">GENBEREGNET</div>
                        <div className="amount" style={{ fontSize: 20, marginTop: 4 }}>{formatDKK(totalRecalc)}</div>
                        <Text type="secondary" style={{ fontSize: 12 }}>{recalc.recalculated!.totalEnergyKwh.toFixed(2)} kWh</Text>
                      </Card>
                    </Col>
                    <Col xs={12} md={6}>
                      <Card size="small" style={{ borderRadius: 10, background: Math.abs(totalPct) < 2 ? '#f0fdf4' : '#fefce8' }}>
                        <div className="micro-label">AFVIGELSE (BELØB)</div>
                        <div className="amount" style={{ fontSize: 20, marginTop: 4, color: diffColor(totalDiff) }}>
                          {(totalDiff >= 0 ? '+' : '') + formatDKK(totalDiff)}
                        </div>
                        <Text style={{ fontSize: 13, fontWeight: 600, color: diffColor(totalDiff) }}>
                          {totalPct >= 0 ? '+' : ''}{totalPct.toFixed(1)}%
                        </Text>
                      </Card>
                    </Col>
                    <Col xs={12} md={6}>
                      <Card size="small" style={{ borderRadius: 10, background: Math.abs(energyPct) < 2 ? '#f0fdf4' : '#fefce8' }}>
                        <div className="micro-label">AFVIGELSE (ENERGI)</div>
                        <div className="amount" style={{ fontSize: 20, marginTop: 4, color: diffColor(energyDiff) }}>
                          {(energyDiff >= 0 ? '+' : '')}{energyDiff.toFixed(2)} kWh
                        </div>
                        <Text style={{ fontSize: 13, fontWeight: 600, color: diffColor(energyDiff) }}>
                          {energyPct >= 0 ? '+' : ''}{energyPct.toFixed(1)}%
                        </Text>
                      </Card>
                    </Col>
                  </Row>

                  {/* Category breakdown cards */}
                  <Row gutter={12}>
                    {catSubtotals.map(cs => {
                      const cc = categoryConfig[cs.cat];
                      return (
                        <Col key={cs.cat} xs={12} md={6}>
                          <div style={{
                            borderRadius: 8, padding: '10px 12px',
                            background: cc.bg, borderLeft: `3px solid ${cc.color}`,
                          }}>
                            <div style={{ fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: cc.color }}>{cc.label}</div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 4 }}>
                              <Text className="tnum" style={{ fontSize: 12 }}>{formatDKK(cs.origSum)}</Text>
                              <Text className="tnum" style={{ fontSize: 12 }}>→ {formatDKK(cs.recalcSum)}</Text>
                            </div>
                            <div style={{ textAlign: 'right', marginTop: 2 }}>
                              {diffPctBadge(cs.diff, cs.pct)}
                            </div>
                          </div>
                        </Col>
                      );
                    })}
                  </Row>

                  {/* Grouped line-by-line comparison table */}
                  <div style={{ border: '1px solid #e5e7eb', borderRadius: 10, overflow: 'hidden' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
                      <thead>
                        <tr style={{ background: '#f8fafb', borderBottom: '2px solid #e5e7eb' }}>
                          <th style={{ textAlign: 'left', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Linje</th>
                          <th style={{ textAlign: 'right', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', width: 110 }}>Original</th>
                          <th style={{ textAlign: 'right', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', width: 110 }}>Genberegnet</th>
                          <th style={{ textAlign: 'right', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', width: 140 }}>Afvigelse</th>
                        </tr>
                      </thead>
                      <tbody>
                        {catSubtotals.map((cs, ci) => {
                          const cc = categoryConfig[cs.cat];
                          return [
                            // Category header row
                            <tr key={`cat-${cs.cat}`} style={{ background: cc.bg + '40', borderTop: ci > 0 ? '2px solid #e5e7eb' : undefined }}>
                              <td colSpan={4} style={{ padding: '6px 12px' }}>
                                <span style={{ fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: cc.color }}>
                                  {cc.label}
                                </span>
                              </td>
                            </tr>,
                            // Line rows (with expandable detail)
                            ...cs.rows.flatMap((r) => {
                              const isExpanded = expandedCompRows.has(r.key);
                              const hasDetail = r.origQty !== null || r.recalcQty !== null;
                              const toggleExpand = () => {
                                if (!hasDetail) return;
                                setExpandedCompRows(prev => {
                                  const next = new Set(prev);
                                  if (next.has(r.key)) next.delete(r.key);
                                  else next.add(r.key);
                                  return next;
                                });
                              };
                              return [
                                <tr key={r.key}
                                  onClick={toggleExpand}
                                  style={{ borderBottom: '1px solid #f3f4f6', cursor: hasDetail ? 'pointer' : undefined }}
                                >
                                  <td style={{ padding: '6px 12px 6px 24px' }}>
                                    {hasDetail && (
                                      <span style={{ display: 'inline-block', width: 16, fontSize: 10, color: '#9ca3af', transition: 'transform 0.2s', transform: isExpanded ? 'rotate(90deg)' : 'none' }}>▶</span>
                                    )}
                                    <span>{r.description}</span>
                                    {r.onlyOrig && <Tag style={{ marginLeft: 6, background: '#f3f4f6', color: '#6b7280', border: '1px solid #d1d5db', fontSize: 10 }}>kun original</Tag>}
                                    {r.onlyRecalc && <Tag style={{ marginLeft: 6, background: '#dbeafe', color: '#1e40af', border: '1px solid #93c5fd', fontSize: 10 }}>kun genberegnet</Tag>}
                                  </td>
                                  <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', color: r.origAmt === 0 ? '#9ca3af' : undefined }}>
                                    {r.origAmt !== 0 ? formatDKK(r.origAmt) : '—'}
                                  </td>
                                  <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', color: r.recalcAmt === 0 ? '#9ca3af' : undefined }}>
                                    {r.recalcAmt !== 0 ? formatDKK(r.recalcAmt) : '—'}
                                  </td>
                                  <td style={{ textAlign: 'right', padding: '6px 12px' }}>
                                    {diffPctBadge(r.diff, r.diffPct)}
                                  </td>
                                </tr>,
                                // Expanded detail row
                                isExpanded && hasDetail && (
                                  <tr key={`${r.key}-detail`}>
                                    <td colSpan={4} style={{ padding: '0 12px 12px 24px', background: '#fafbfc' }}>
                                      {renderLineDetail(r)}
                                    </td>
                                  </tr>
                                ),
                              ].filter(Boolean);
                            }),
                            // Category subtotal
                            <tr key={`sub-${cs.cat}`} style={{ background: '#f8fafb', borderBottom: '1px solid #e5e7eb' }}>
                              <td style={{ padding: '6px 12px', fontWeight: 600, fontSize: 12, color: '#374151' }}>Subtotal {cc.label.toLowerCase()}</td>
                              <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', fontWeight: 600 }}>{formatDKK(cs.origSum)}</td>
                              <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', fontWeight: 600 }}>{formatDKK(cs.recalcSum)}</td>
                              <td style={{ textAlign: 'right', padding: '6px 12px', fontWeight: 600 }}>{diffPctBadge(cs.diff, cs.pct)}</td>
                            </tr>,
                          ];
                        })}
                        {/* Grand total */}
                        <tr style={{ background: '#f0f4f8', borderTop: '2px solid #cbd5e1' }}>
                          <td style={{ padding: '10px 12px', fontWeight: 700, fontSize: 14 }}>Total</td>
                          <td className="tnum" style={{ textAlign: 'right', padding: '10px 12px', fontWeight: 700, fontSize: 14 }}>{formatDKK(totalOrig)}</td>
                          <td className="tnum" style={{ textAlign: 'right', padding: '10px 12px', fontWeight: 700, fontSize: 14 }}>{formatDKK(totalRecalc)}</td>
                          <td style={{ textAlign: 'right', padding: '10px 12px', fontWeight: 700, fontSize: 14 }}>{diffPctBadge(totalDiff, totalPct)}</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <Alert
                    type="info" showIcon icon={<InfoCircleOutlined />}
                    message="Genberegningen er ikke gemt"
                    description="Denne genberegning bruger WattsOns beregningsmotor med aktuelle priser og tidsserier. Resultatet gemmes ikke — det er kun til sammenligning. Energiforskelle skyldes primært opløsningsforskel (PT15M observationer vs. Xellents timefakturering)."
                    style={{ borderRadius: 8 }}
                  />
                </>
                );
              })()}

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
