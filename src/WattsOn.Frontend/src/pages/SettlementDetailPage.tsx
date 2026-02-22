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
import type { SettlementDocument, SettlementDocumentLine, RecalcResult, RecalcLine, LineDetails, DailyDetail, MigratedHourlyEntry } from '../api/client';
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
  const [expandedSettleRows, setExpandedSettleRows] = useState<Set<number>>(new Set());
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
        origDetails: orig?.details ?? null,
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
  // Shared detail renderer for both settlement lines and recalc comparison rows
  // Check if a line has visible expandable detail
  const hasVisibleDetail = (d: LineDetails | undefined | null, daily?: DailyDetail[] | null): boolean => {
    if (!d) return false;
    if (d.type === 'tarif' && (d.daily || daily) && ((d.daily ?? daily)!.length > 0)) return true;
    if (d.type === 'abonnement' && d.days && d.dailyRate) return true;
    if (d.type === 'spot' && (d.totalHours || d.daily || daily)) return true;
    if (d.type === 'margin' && d.ratePerKwh !== undefined) return true;
    return false;
  };

  const renderBreakdownDetail = (d: LineDetails | undefined | null, daily?: DailyDetail[] | null) => {
    if (!d) return null;

    return (
      <div style={{ padding: '4px 0' }}>
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
        {d.type === 'margin' && (
          <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
            Sats: {fmtRate(d.ratePerKwh!)} DKK/kWh
          </div>
        )}

        {/* Daily/hourly breakdown — rendered flat, no collapsible wrapper */}
        {(d.daily ?? daily) && (() => {
          const dayData = (d.daily ?? daily)!;
          if (!dayData || dayData.length === 0) return null;
          return (
            <div style={{ marginTop: 8, maxHeight: 420, overflowY: 'auto', fontSize: 12 }}>
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
                  {dayData.map((day) => {
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
          );
        })()}
      </div>
    );
  };

  // Recalc comparison: shows both original and recalculated hourly data
  const renderLineDetail = (row: ReturnType<typeof buildComparisonRows>[number]) => {
    const recalcDaily = row.details?.daily;
    const origDaily = row.origDetails?.daily;

    // If we have hourly data from both sides, show side-by-side comparison
    if (recalcDaily && recalcDaily.length > 0 && origDaily && origDaily.length > 0) {
      // Build lookup: date → hour → origHour
      const origLookup = new Map<string, Map<number, { kwh: number; rate: number; amount: number }>>();
      for (const day of origDaily) {
        const hourMap = new Map<number, { kwh: number; rate: number; amount: number }>();
        for (const h of day.hours) hourMap.set(h.hour, h);
        origLookup.set(day.date, hourMap);
      }

      return (
        <div style={{ padding: '4px 0' }}>
          {/* Non-hourly details (subscription, margin, spot stats) */}
          {row.details?.type === 'abonnement' && row.details.days != null && row.details.dailyRate != null && (
            <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
              {row.details.days} dage × {fmtRate(row.details.dailyRate)} DKK/dag = {formatDKK(row.details.days * row.details.dailyRate)}
            </div>
          )}
          {row.details?.type === 'spot' && row.details.totalHours != null && (
            <div style={{ marginTop: 4 }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 4 }}>
                Spotprisstatistik
              </div>
              <div style={{ fontSize: 12, display: 'flex', gap: 16 }}>
                <span><Text type="secondary">Timer: </Text><span className="tnum">{row.details.hoursWithPrice}/{row.details.totalHours}</span></span>
                <span><Text type="secondary">Gns: </Text><span className="tnum">{fmtRate(row.details.avgRate!)}</span></span>
                <span><Text type="secondary">Min: </Text><span className="tnum">{fmtRate(row.details.minRate!)}</span></span>
                <span><Text type="secondary">Maks: </Text><span className="tnum">{fmtRate(row.details.maxRate!)}</span></span>
                <Text type="secondary">DKK/kWh</Text>
              </div>
            </div>
          )}
          {row.details?.type === 'margin' && row.details.ratePerKwh !== undefined && (
            <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
              Sats: {fmtRate(row.details.ratePerKwh)} DKK/kWh
            </div>
          )}

          {/* Hourly comparison table */}
          <div style={{ marginTop: 8, maxHeight: 420, overflowY: 'auto', fontSize: 12 }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ borderBottom: '2px solid #e5e7eb', position: 'sticky', top: 0, background: '#fff', zIndex: 1 }}>
                  <th style={{ textAlign: 'left', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Dato / Time</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Orig. sats</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Ny sats</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Orig. beløb</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Nyt beløb</th>
                </tr>
              </thead>
              <tbody>
                {recalcDaily.map((day) => {
                  const dayLabel = new Date(day.date + 'T12:00:00').toLocaleDateString('da-DK', { day: 'numeric', month: 'short', year: 'numeric' });
                  const origDay = origDaily.find(d => d.date === day.date);
                  return [
                    <tr key={`day-${day.date}`} style={{ background: '#f8fafb', borderTop: '1px solid #e5e7eb' }}>
                      <td style={{ padding: '5px 6px', fontWeight: 600, color: '#374151' }}>{dayLabel}</td>
                      <td style={{ padding: '5px 6px' }}></td>
                      <td style={{ padding: '5px 6px' }}></td>
                      <td className="tnum" style={{ padding: '5px 6px', textAlign: 'right', fontWeight: 600, color: '#6b7280' }}>{origDay ? formatDKK(origDay.amount) : '—'}</td>
                      <td className="tnum" style={{ padding: '5px 6px', textAlign: 'right', fontWeight: 600, color: '#374151' }}>{formatDKK(day.amount)}</td>
                    </tr>,
                    ...day.hours.map((h) => {
                      const origHour = origLookup.get(day.date)?.get(h.hour);
                      const rateDiff = origHour ? h.rate - origHour.rate : null;
                      const rateChanged = rateDiff !== null && Math.abs(rateDiff) > 0.000001;
                      return (
                        <tr key={`${day.date}-${h.hour}`} style={{ borderBottom: '1px solid #f3f4f6' }}>
                          <td style={{ padding: '3px 6px 3px 20px', color: '#6b7280' }}>{String(h.hour).padStart(2, '0')}:00</td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right', color: rateChanged ? '#d97706' : '#6b7280' }}>
                            {origHour ? fmtRate(origHour.rate) : '—'}
                          </td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right', color: rateChanged ? '#d97706' : undefined }}>
                            {fmtRate(h.rate)}
                          </td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right', color: '#6b7280' }}>
                            {origHour ? formatDKK(origHour.amount) : '—'}
                          </td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right' }}>{formatDKK(h.amount)}</td>
                        </tr>
                      );
                    }),
                  ];
                })}
              </tbody>
            </table>
          </div>
        </div>
      );
    }

    // One side has hourly data — show with original unit price as reference
    const dailyData = row.details?.daily;
    if (dailyData && dailyData.length > 0 && row.origUnit !== null) {
      return (
        <div style={{ padding: '4px 0' }}>
          {row.details?.type === 'spot' && row.details.totalHours != null && (
            <div style={{ marginTop: 4, marginBottom: 8 }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6b7280', marginBottom: 4 }}>
                Spotprisstatistik
              </div>
              <div style={{ fontSize: 12, display: 'flex', gap: 16 }}>
                <span><Text type="secondary">Timer: </Text><span className="tnum">{row.details.hoursWithPrice}/{row.details.totalHours}</span></span>
                <span><Text type="secondary">Gns: </Text><span className="tnum">{fmtRate(row.details.avgRate!)}</span></span>
                <span><Text type="secondary">Min: </Text><span className="tnum">{fmtRate(row.details.minRate!)}</span></span>
                <span><Text type="secondary">Maks: </Text><span className="tnum">{fmtRate(row.details.maxRate!)}</span></span>
                <Text type="secondary">DKK/kWh</Text>
              </div>
            </div>
          )}
          <div style={{ marginTop: 8, maxHeight: 420, overflowY: 'auto', fontSize: 12 }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ borderBottom: '2px solid #e5e7eb', position: 'sticky', top: 0, background: '#fff', zIndex: 1 }}>
                  <th style={{ textAlign: 'left', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Dato / Time</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>kWh</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Orig. sats</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Ny sats</th>
                  <th style={{ textAlign: 'right', padding: '4px 6px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Beløb</th>
                </tr>
              </thead>
              <tbody>
                {dailyData.map((day) => {
                  const dayLabel = new Date(day.date + 'T12:00:00').toLocaleDateString('da-DK', { day: 'numeric', month: 'short', year: 'numeric' });
                  return [
                    <tr key={`day-${day.date}`} style={{ background: '#f8fafb', borderTop: '1px solid #e5e7eb' }}>
                      <td style={{ padding: '5px 6px', fontWeight: 600, color: '#374151' }}>{dayLabel}</td>
                      <td className="tnum" style={{ padding: '5px 6px', textAlign: 'right', fontWeight: 600, color: '#374151' }}>{day.kwh.toFixed(2)}</td>
                      <td style={{ padding: '5px 6px' }}></td>
                      <td style={{ padding: '5px 6px' }}></td>
                      <td className="tnum" style={{ padding: '5px 6px', textAlign: 'right', fontWeight: 600, color: '#374151' }}>{formatDKK(day.amount)}</td>
                    </tr>,
                    ...day.hours.map((h) => {
                      const rateChanged = Math.abs(h.rate - row.origUnit!) > 0.000001;
                      return (
                        <tr key={`${day.date}-${h.hour}`} style={{ borderBottom: '1px solid #f3f4f6' }}>
                          <td style={{ padding: '3px 6px 3px 20px', color: '#6b7280' }}>{String(h.hour).padStart(2, '0')}:00</td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right' }}>{h.kwh.toFixed(3)}</td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right', color: '#6b7280' }}>
                            {fmtRate(row.origUnit!)}
                          </td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right', color: rateChanged ? '#d97706' : undefined }}>
                            {fmtRate(h.rate)}
                          </td>
                          <td className="tnum" style={{ padding: '3px 6px', textAlign: 'right' }}>{formatDKK(h.amount)}</td>
                        </tr>
                      );
                    }),
                  ];
                })}
              </tbody>
            </table>
          </div>
        </div>
      );
    }

    // No hourly data — use shared renderer
    return renderBreakdownDetail(row.details, row.details?.daily);
  };

  // Settlement line: just delegates to shared renderer
  const renderSettlementLineDetail = (line: SettlementDocumentLine) =>
    renderBreakdownDetail(line.details, line.details?.daily);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!doc) return null;

  const config = docTypeConfig[doc.documentType] || docTypeConfig.settlement;
  const canConfirm = doc.status === 'Calculated';

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

      {/* Line items — grouped by category */}
      <Card title="Linjer" style={{ borderRadius: 12 }}>
        {(() => {
          // Classify settlement lines into categories
          const classifySettleLine = (line: SettlementDocumentLine): string => {
            if (line.source === 'SpotPrice') return 'spot';
            if (line.source === 'SupplierMargin') return 'margin';
            const d = line.description.toLowerCase();
            if (d.includes('abo') || d.includes('abonnement') || line.details?.type === 'abonnement') return 'subscription';
            return 'tariff';
          };

          const categories = ['tariff', 'subscription', 'spot', 'margin'] as const;
          const grouped = categories.map(cat => {
            const lines = doc.lines.filter(l => classifySettleLine(l) === cat);
            if (lines.length === 0) return null;
            const sum = lines.reduce((s, l) => s + l.lineAmount, 0);
            const taxSum = lines.reduce((s, l) => s + l.taxAmount, 0);
            return { cat, lines, sum, taxSum };
          }).filter(Boolean) as { cat: string; lines: SettlementDocumentLine[]; sum: number; taxSum: number }[];

          return (
            <div style={{ border: '1px solid #e5e7eb', borderRadius: 10, overflow: 'hidden' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
                <thead>
                  <tr style={{ background: '#f8fafb', borderBottom: '2px solid #e5e7eb' }}>
                    <th style={{ textAlign: 'left', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Beskrivelse</th>
                    <th style={{ textAlign: 'right', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', width: 100 }}>Mængde</th>
                    <th style={{ textAlign: 'right', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', width: 100 }}>Enhedspris</th>
                    <th style={{ textAlign: 'right', padding: '8px 12px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase', width: 110 }}>Beløb</th>
                  </tr>
                </thead>
                <tbody>
                  {grouped.map((g, gi) => {
                    const cc = categoryConfig[g.cat];
                    return [
                      <tr key={`cat-${g.cat}`} style={{ background: cc.bg + '40', borderTop: gi > 0 ? '2px solid #e5e7eb' : undefined }}>
                        <td colSpan={4} style={{ padding: '6px 12px' }}>
                          <span style={{ fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: cc.color }}>
                            {cc.label}
                          </span>
                        </td>
                      </tr>,
                      ...g.lines.flatMap((line) => {
                        const canExpand = hasVisibleDetail(line.details, line.details?.daily);
                        const isExpanded = canExpand && expandedSettleRows.has(line.lineNumber);
                        const toggleExpand = canExpand ? () => {
                          setExpandedSettleRows(prev => {
                            const next = new Set(prev);
                            if (next.has(line.lineNumber)) next.delete(line.lineNumber);
                            else next.add(line.lineNumber);
                            return next;
                          });
                        } : undefined;
                        const isSub = line.details?.type === 'abonnement';
                        const unit = isSub ? 'dage' : 'kWh';
                        return [
                          <tr key={line.lineNumber} onClick={toggleExpand} style={{ borderBottom: '1px solid #f3f4f6', cursor: canExpand ? 'pointer' : undefined }}>
                            <td style={{ padding: '6px 12px 6px 24px' }}>
                              {canExpand && <span style={{ display: 'inline-block', width: 16, fontSize: 10, color: '#9ca3af', transition: 'transform 0.2s', transform: isExpanded ? 'rotate(90deg)' : 'none' }}>▶</span>}
                              <span>{line.description}</span>
                              {line.details?.totalHours != null && (
                                <span style={{ marginLeft: 8, fontSize: 11, color: '#9ca3af' }}>
                                  {line.details.totalHours} timer
                                </span>
                              )}
                            </td>
                            <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', color: '#6b7280' }}>
                              {line.quantity.toFixed(2)} {unit}
                            </td>
                            <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', color: '#6b7280' }}>
                              {fmtRate(line.unitPrice)} DKK
                            </td>
                            <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', fontWeight: 500, color: line.lineAmount < 0 ? '#059669' : undefined }}>
                              {formatDKK(line.lineAmount)}
                            </td>
                          </tr>,
                          isExpanded && (
                            <tr key={`${line.lineNumber}-detail`}>
                              <td colSpan={4} style={{ padding: '0 12px 12px 40px', background: '#fafbfc' }}>
                                {renderSettlementLineDetail(line)}
                              </td>
                            </tr>
                          ),
                        ].filter(Boolean);
                      }),
                      <tr key={`sub-${g.cat}`} style={{ background: '#f8fafb', borderBottom: '1px solid #e5e7eb' }}>
                        <td colSpan={3} style={{ padding: '6px 12px', fontWeight: 600, fontSize: 12, color: '#374151' }}>Subtotal {cc.label.toLowerCase()}</td>
                        <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px', fontWeight: 600 }}>{formatDKK(g.sum)}</td>
                      </tr>,
                    ];
                  })}
                  {/* Tax summary */}
                  {doc.taxSummary.map(tax => (
                    <tr key={`tax-${tax.taxCategory}`} style={{ borderBottom: '1px solid #f3f4f6' }}>
                      <td colSpan={3} style={{ padding: '6px 12px', textAlign: 'right', color: '#6b7280' }}>
                        Moms af {formatDKK(tax.taxableAmount)}
                      </td>
                      <td className="tnum" style={{ textAlign: 'right', padding: '6px 12px' }}>{formatDKK(tax.taxAmount)}</td>
                    </tr>
                  ))}
                  {/* Grand total */}
                  <tr style={{ background: '#f0f4f8', borderTop: '2px solid #cbd5e1' }}>
                    <td colSpan={3} style={{ padding: '10px 12px', fontWeight: 700, fontSize: 14, textAlign: 'right' }}>Total inkl. moms</td>
                    <td className="tnum" style={{ textAlign: 'right', padding: '10px 12px', fontWeight: 700, fontSize: 14, color: doc.totalInclVat < 0 ? '#059669' : undefined }}>
                      {formatDKK(doc.totalInclVat)}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          );
        })()}
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
                              const canExpand = hasVisibleDetail(r.details, r.details?.daily);
                              const isExpanded = canExpand && expandedCompRows.has(r.key);
                              const toggleExpand = canExpand ? () => {
                                setExpandedCompRows(prev => {
                                  const next = new Set(prev);
                                  if (next.has(r.key)) next.delete(r.key);
                                  else next.add(r.key);
                                  return next;
                                });
                              } : undefined;
                              return [
                                <tr key={r.key}
                                  onClick={toggleExpand}
                                  style={{ borderBottom: '1px solid #f3f4f6', cursor: canExpand ? 'pointer' : undefined }}
                                >
                                  <td style={{ padding: '6px 12px 6px 24px' }}>
                                    {canExpand && (
                                      <span style={{ display: 'inline-block', width: 16, fontSize: 10, color: '#9ca3af', transition: 'transform 0.2s', transform: isExpanded ? 'rotate(90deg)' : 'none' }}>▶</span>
                                    )}
                                    <span>{r.description}</span>
                                    {r.details?.totalHours != null && (
                                      <span style={{ marginLeft: 8, fontSize: 11, color: '#9ca3af' }}>
                                        {r.details.totalHours} timer
                                      </span>
                                    )}
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
                                isExpanded && canExpand && (
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

                  {/* Energy (kWh) hourly breakdown — merged from both sources with diff navigation */}
                  {(() => {
                    const kwhLine = compRows.find(r => r.details?.daily && r.details.daily.length > 0);
                    if (!kwhLine?.details?.daily && !recalc.migratedHourly?.length) return null;

                    const dkTz = 'Europe/Copenhagen';

                    // Build new (recalculated) lookup: "YYYY-MM-DD|HH" → kwh
                    const newHourLookup = new Map<string, number>();
                    const newDaySums = new Map<string, number>();
                    if (kwhLine?.details?.daily) {
                      for (const d of kwhLine.details.daily) {
                        for (const h of d.hours) {
                          newHourLookup.set(`${d.date}|${h.hour}`, h.kwh);
                        }
                        newDaySums.set(d.date, d.kwh);
                      }
                    }

                    // Build orig (Xellent) lookup
                    const origHourLookup = new Map<string, number>();
                    const origDaySums = new Map<string, number>();
                    if (recalc.migratedHourly && recalc.migratedHourly.length > 0) {
                      for (const h of recalc.migratedHourly) {
                        const lt = new Date(h.t).toLocaleString('sv-SE', { timeZone: dkTz });
                        const [dateStr, timeStr] = lt.split(' ');
                        const hour = parseInt(timeStr.split(':')[0], 10);
                        const key = `${dateStr}|${hour}`;
                        origHourLookup.set(key, (origHourLookup.get(key) ?? 0) + h.k);
                        origDaySums.set(dateStr, (origDaySums.get(dateStr) ?? 0) + h.k);
                      }
                    }
                    const hasOrigHourly = origHourLookup.size > 0;

                    // Merge all dates + hours from both sources
                    const allDates = new Set([...newDaySums.keys(), ...origDaySums.keys()]);
                    const sortedDates = Array.from(allDates).sort();

                    type MergedHour = { hour: number; origKwh: number | null; newKwh: number | null; diff: boolean };
                    type MergedDay = { date: string; hours: MergedHour[]; origTotal: number; newTotal: number; diff: boolean };
                    const mergedDays: MergedDay[] = sortedDates.map(date => {
                      // Collect all hours for this date from both sources
                      const allHours = new Set<number>();
                      for (const [key] of origHourLookup) {
                        const [d, h] = key.split('|');
                        if (d === date) allHours.add(parseInt(h, 10));
                      }
                      for (const [key] of newHourLookup) {
                        const [d, h] = key.split('|');
                        if (d === date) allHours.add(parseInt(h, 10));
                      }
                      const sortedHours = Array.from(allHours).sort((a, b) => a - b);

                      const hours: MergedHour[] = sortedHours.map(hour => {
                        const key = `${date}|${hour}`;
                        const orig = origHourLookup.get(key) ?? null;
                        const nw = newHourLookup.get(key) ?? null;
                        const diff = orig !== null && nw !== null
                          ? Math.abs(orig - nw) > 0.0001
                          : orig !== nw; // one side missing
                        return { hour, origKwh: orig, newKwh: nw, diff };
                      });

                      const origTotal = origDaySums.get(date) ?? 0;
                      const newTotal = newDaySums.get(date) ?? 0;
                      const diff = Math.abs(origTotal - newTotal) > 0.001;
                      return { date, hours, origTotal, newTotal, diff };
                    });

                    const totalOrigKwh = Array.from(origDaySums.values()).reduce((s, v) => s + v, 0);
                    const totalNewKwh = Array.from(newDaySums.values()).reduce((s, v) => s + v, 0);
                    const totalHours = mergedDays.reduce((s, d) => s + d.hours.length, 0);
                    const energyDiff = totalNewKwh - totalOrigKwh;
                    const diffCount = mergedDays.reduce((s, d) => s + d.hours.filter(h => h.diff).length, 0);

                    const jumpToDiff = (direction: 'next' | 'prev') => {
                      const container = document.getElementById('energy-kwh-scroll');
                      if (!container) return;
                      const diffRows = container.querySelectorAll<HTMLElement>('[data-kwh-diff]');
                      if (diffRows.length === 0) return;
                      const scrollTop = container.scrollTop;
                      const mid = scrollTop + container.clientHeight / 2;
                      let currentIdx = 0;
                      for (let i = 0; i < diffRows.length; i++) {
                        if (diffRows[i].offsetTop <= mid) currentIdx = i;
                      }
                      const targetIdx = direction === 'next'
                        ? Math.min(currentIdx + 1, diffRows.length - 1)
                        : Math.max(currentIdx - 1, 0);
                      diffRows[targetIdx].scrollIntoView({ behavior: 'smooth', block: 'center' });
                    };

                    return (
                      <div style={{ border: '1px solid #e5e7eb', borderRadius: 10, overflow: 'hidden' }}>
                        <div style={{ padding: '10px 12px', background: '#f0f9ff', borderBottom: '1px solid #e5e7eb', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
                          <div>
                            <span style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#0369a1' }}>
                              Energi (kWh) — {totalHours} timer
                            </span>
                            <span style={{ marginLeft: 12, fontSize: 11, color: '#6b7280' }}>
                              Xellent: {totalOrigKwh.toFixed(2)} kWh → WattsOn: {totalNewKwh.toFixed(2)} kWh
                              <span style={{ marginLeft: 6, color: Math.abs(energyDiff) < 0.01 ? '#059669' : '#d97706' }}>
                                ({(energyDiff >= 0 ? '+' : '')}{energyDiff.toFixed(2)} kWh)
                              </span>
                            </span>
                          </div>
                          {diffCount > 0 && (
                            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                              <span style={{ fontSize: 11, color: '#d97706', fontWeight: 600 }}>{diffCount} afvigelser</span>
                              <button onClick={() => jumpToDiff('prev')}
                                style={{ border: '1px solid #d1d5db', borderRadius: 4, background: '#fff', padding: '2px 8px', cursor: 'pointer', fontSize: 11, color: '#374151' }}
                              >▲ Forrige</button>
                              <button onClick={() => jumpToDiff('next')}
                                style={{ border: '1px solid #d1d5db', borderRadius: 4, background: '#fff', padding: '2px 8px', cursor: 'pointer', fontSize: 11, color: '#374151' }}
                              >▼ Næste</button>
                            </div>
                          )}
                        </div>
                        <div id="energy-kwh-scroll" style={{ maxHeight: 380, overflowY: 'auto', fontSize: 12 }}>
                          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                            <thead>
                              <tr style={{ borderBottom: '2px solid #e5e7eb', position: 'sticky', top: 0, background: '#fff', zIndex: 1 }}>
                                <th style={{ textAlign: 'left', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Dato / Time</th>
                                <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>Xellent kWh</th>
                                <th style={{ textAlign: 'right', padding: '4px 8px', fontWeight: 600, color: '#6b7280', fontSize: 10, textTransform: 'uppercase' }}>WattsOn kWh</th>
                              </tr>
                            </thead>
                            <tbody>
                              {mergedDays.map((day) => {
                                const dayLabel = new Date(day.date + 'T12:00:00').toLocaleDateString('da-DK', { day: 'numeric', month: 'short', year: 'numeric' });
                                return [
                                  <tr key={`ekwh-${day.date}`} style={{ background: day.diff ? '#fef3c7' : '#f8fafb', borderTop: '1px solid #e5e7eb' }}>
                                    <td style={{ padding: '5px 8px', fontWeight: 600, color: day.diff ? '#92400e' : '#374151' }}>{dayLabel}</td>
                                    <td className="tnum" style={{ padding: '5px 8px', textAlign: 'right', fontWeight: 600, color: day.diff ? '#92400e' : '#6b7280' }}>
                                      {day.origTotal > 0 ? day.origTotal.toFixed(2) : '—'}
                                    </td>
                                    <td className="tnum" style={{ padding: '5px 8px', textAlign: 'right', fontWeight: 600, color: day.diff ? '#92400e' : '#374151' }}>
                                      {day.newTotal > 0 ? day.newTotal.toFixed(2) : '—'}
                                    </td>
                                  </tr>,
                                  ...day.hours.map((h) => (
                                    <tr key={`ekwh-${day.date}-${h.hour}`}
                                      {...(h.diff ? { 'data-kwh-diff': '' } as any : {})}
                                      style={{ borderBottom: '1px solid #f3f4f6', background: h.diff ? '#fef9c3' : undefined }}
                                    >
                                      <td style={{ padding: '3px 8px 3px 24px', color: h.diff ? '#92400e' : '#6b7280' }}>
                                        {String(h.hour).padStart(2, '0')}:00
                                      </td>
                                      <td className="tnum" style={{ padding: '3px 8px', textAlign: 'right', color: h.diff ? '#d97706' : '#6b7280' }}>
                                        {h.origKwh !== null ? h.origKwh.toFixed(3) : '—'}
                                      </td>
                                      <td className="tnum" style={{ padding: '3px 8px', textAlign: 'right', color: h.diff ? '#d97706' : undefined }}>
                                        {h.newKwh !== null ? h.newKwh.toFixed(3) : '—'}
                                      </td>
                                    </tr>
                                  )),
                                ];
                              })}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    );
                  })()}

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
