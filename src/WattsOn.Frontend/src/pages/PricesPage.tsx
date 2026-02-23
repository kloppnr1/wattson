import { useEffect, useState, useCallback } from 'react';
import {
  Card, Table, Spin, Alert, Space, Typography, Row, Col, Statistic, Tag,
  Tabs, Empty, DatePicker, Collapse,
} from 'antd';
import {
  DollarOutlined, ThunderboltOutlined, BankOutlined,
  AreaChartOutlined, CalendarOutlined,
  RightOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { PriceSummary, PriceDetail } from '../api/client';
import { getPrices, getPrice, getSupplierIdentities } from '../api/client';
import { formatDate, formatTime, formatTimeUtc, formatPrice4 } from '../utils/format';
import api from '../api/client';

const { Text, Title } = Typography;

const typeColors: Record<string, string> = {
  Tarif: 'teal',
  Gebyr: 'orange',
  Abonnement: 'purple',
};

interface SpotRow {
  hourUtc: string;
  dk1: number | null;
  dk2: number | null;
}

interface SpotLatest {
  totalRecords: number;
  dk1: { hourUtc: string; spotPriceDkkPerKwh: number } | null;
  dk2: { hourUtc: string; spotPriceDkkPerKwh: number } | null;
}

interface MarginRow {
  id: string;
  supplierProductId: string;
  productName: string;
  pricingModel: string;
  validFrom: string;
  priceDkkPerKwh: number;
}

export default function PricesPage() {
  const [prices, setPrices] = useState<PriceSummary[]>([]);
  const [ourGlns, setOurGlns] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [detailCache, setDetailCache] = useState<Record<string, PriceDetail>>({});
  const [detailLoading, setDetailLoading] = useState<Record<string, boolean>>({});

  // Shared date selector
  const [selectedDate, setSelectedDate] = useState<Dayjs>(dayjs());

  // Spot prices
  const [spotLatest, setSpotLatest] = useState<SpotLatest | null>(null);
  const [spotRows, setSpotRows] = useState<SpotRow[]>([]);
  const [spotLoading, setSpotLoading] = useState(false);

  // Supplier margins
  const [marginRows, setMarginRows] = useState<MarginRow[]>([]);

  // Initial load
  useEffect(() => {
    Promise.all([
      getPrices(),
      getSupplierIdentities(),
      api.get<SpotLatest>('/spot-prices/latest'),
      api.get<{ totalRecords: number; rows: MarginRow[] }>('/supplier-margins'),
    ])
      .then(([pricesRes, identitiesRes, latestRes, marginsRes]) => {
        setPrices(pricesRes.data);
        setOurGlns(new Set(identitiesRes.data.map(si => si.gln)));
        setSpotLatest(latestRes.data);
        setMarginRows(marginsRes.data.rows);
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  // Fetch spot prices when date changes
  const fetchSpotForDate = useCallback(async (date: Dayjs) => {
    setSpotLoading(true);
    try {
      const res = await api.get<{ totalRecords: number; rows: SpotRow[] }>(`/spot-prices?date=${date.format('YYYY-MM-DD')}`);
      setSpotRows(res.data.rows);
    } catch { setSpotRows([]); }
    finally { setSpotLoading(false); }
  }, []);

  useEffect(() => { fetchSpotForDate(selectedDate); }, [selectedDate, fetchSpotForDate]);

  // Lazy-load price detail when a DataHub panel is expanded
  const loadDetail = useCallback(async (priceId: string) => {
    if (detailCache[priceId] || detailLoading[priceId]) return;
    setDetailLoading(prev => ({ ...prev, [priceId]: true }));
    try {
      const res = await getPrice(priceId);
      setDetailCache(prev => ({ ...prev, [priceId]: res.data }));
    } catch { /* silently fail */ }
    finally { setDetailLoading(prev => ({ ...prev, [priceId]: false })); }
  }, [detailCache, detailLoading]);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message="Kunne ikke hente priser" description={error} />;

  // Filter out spot prices from the general list (they have their own tab)
  const isSpot = (p: PriceSummary) => p.chargeId.startsWith('SPOT-');
  const nonSpotPrices = prices.filter(p => !isSpot(p));

  // Supplier prices = owned by our GLN(s); DataHub prices = owned by external parties
  void nonSpotPrices.filter(p => ourGlns.has(p.ownerGln)); // supplierPrices — reserved for future use
  const datahubPrices = nonSpotPrices.filter(p => !ourGlns.has(p.ownerGln));

  // ─── Shared collapse panel styles ───
  const panelCardStyle: React.CSSProperties = {
    borderRadius: 10,
    marginBottom: 8,
    border: '1px solid #e5e7eb',
    overflow: 'hidden',
  };

  // ─── Shared: step-function rate table (used by both Supplier + DataHub) ───
  const renderStepFunctionTable = (
    sortedPoints: { key: string; date: string; price: number }[],
    activeIdx: number,
    totalLabel?: string,
  ) => (
    <div style={{ padding: '4px 0' }}>
      <Text type="secondary" style={{ marginBottom: 8, display: 'block', fontSize: 12 }}>
        {sortedPoints.length} {sortedPoints.length === 1 ? 'sats' : 'satser'}
        {totalLabel ? ` · ${totalLabel}` : ''}
        {' · Gældende sats markeret for '}{selectedDate.format('D. MMM YYYY')}
      </Text>
      <Table
        dataSource={sortedPoints.map((pp, idx) => ({ ...pp, _idx: idx }))}
        columns={[
          { title: 'GYLDIG FRA', dataIndex: 'date', key: 'from', width: 120,
            render: (v: string, row: { _idx: number }) => (
              <Text className="tnum" strong={row._idx === activeIdx}
                style={row._idx === activeIdx ? { color: '#0d9488' } : undefined}>
                {formatDate(v)}
              </Text>
            )},
          { title: 'GYLDIG TIL', key: 'to', width: 120,
            render: (_: unknown, row: { _idx: number }) => {
              const next = sortedPoints[row._idx + 1];
              return <Text className="tnum" type="secondary">{next ? formatDate(next.date) : '→'}</Text>;
            }},
          { title: 'DKK/kWh', dataIndex: 'price', key: 'price', align: 'right' as const,
            render: (v: number, row: { _idx: number }) => (
              <Text className="tnum" strong={row._idx === activeIdx}
                style={row._idx === activeIdx ? { color: '#0d9488', fontSize: 14 } : undefined}>
                {formatPrice4(v)}
              </Text>
            )},
          { title: '', key: 'active', width: 80,
            render: (_: unknown, row: { _idx: number }) =>
              row._idx === activeIdx ? <Tag color="teal">Aktiv</Tag> : null },
        ]}
        rowKey="key"
        size="small"
        pagination={false}
      />
    </div>
  );

  // ─── Shared: find active index for step-function rates ───
  const findActiveIndex = (dates: string[]) => {
    const dateStr = selectedDate.format('YYYY-MM-DD');
    for (let i = dates.length - 1; i >= 0; i--) {
      const d = new Date(dates[i]).toLocaleDateString('sv-SE', { timeZone: 'Europe/Copenhagen' });
      if (d <= dateStr) return i;
    }
    return -1;
  };

  // ─── Shared: panel header layout ───
  const renderPanelHeader = (
    tag: { label: string; color: string },
    name: string,
    subtitle?: string,
    count?: number,
    countLabel?: string,
    currentRate?: number,
    extraTags?: React.ReactNode,
  ) => (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', flexWrap: 'wrap' }}>
      <Tag color={tag.color} style={{ margin: 0 }}>{tag.label}</Tag>
      <Text strong style={{ fontSize: 14 }}>{name}</Text>
      {subtitle && <Text type="secondary" ellipsis style={{ flex: 1, minWidth: 80 }}>{subtitle}</Text>}
      {!subtitle && <div style={{ flex: 1 }} />}
      {extraTags}
      {count != null && (
        <Text className="tnum" type="secondary" style={{ fontSize: 12 }}>
          {count} {countLabel || 'satser'}
        </Text>
      )}
      {currentRate != null && (
        <Text strong className="tnum" style={{ fontSize: 15, color: '#0d9488' }}>
          {formatPrice4(currentRate)} <Text type="secondary" style={{ fontSize: 11, fontWeight: 400 }}>DKK/kWh</Text>
        </Text>
      )}
    </div>
  );

  // ─── DataHub price panel content ───
  const renderDatahubDetail = (record: PriceSummary) => {
    const detail = detailCache[record.id];
    if (detailLoading[record.id]) return <Spin size="small" style={{ margin: 16 }} />;
    if (!detail) return <Text type="secondary" style={{ padding: 16, display: 'block' }}>Henter prispunkter…</Text>;

    const isTemplate = detail.priceResolution === 'PT1H';

    if (isTemplate) {
      // Template tariff: points are 24-hour daily templates grouped on quarter start dates.
      // Group into template blocks, then show ALL periods in an accordion.
      const blocks: { startDate: string; points: typeof detail.pricePoints }[] = [];
      const sorted = [...detail.pricePoints].sort((a, b) => a.timestamp.localeCompare(b.timestamp));

      let currentBlock: typeof detail.pricePoints = [];
      let currentDate = '';
      for (const pp of sorted) {
        const dkDate = new Date(pp.timestamp).toLocaleDateString('sv-SE', { timeZone: 'Europe/Copenhagen' });
        if (dkDate !== currentDate) {
          if (currentBlock.length > 0) blocks.push({ startDate: currentDate, points: currentBlock });
          currentBlock = [];
          currentDate = dkDate;
        }
        currentBlock.push(pp);
      }
      if (currentBlock.length > 0) blocks.push({ startDate: currentDate, points: currentBlock });

      // Find the applicable template: latest block start ≤ selected date
      const dateStr = selectedDate.format('YYYY-MM-DD');
      const activeBlockIdx = (() => {
        for (let i = blocks.length - 1; i >= 0; i--) {
          if (blocks[i].startDate <= dateStr) return i;
        }
        return blocks.length - 1;
      })();

      // Global max price across ALL blocks (for consistent bar scaling)
      const globalMax = Math.max(...detail.pricePoints.map(p => p.price));

      // Build 24-hour table for a single block
      const renderTemplateBlock = (block: typeof blocks[0]) => {
        const blockPoints = [...block.points].sort((a, b) => a.timestamp.localeCompare(b.timestamp));
        const uniqueRates = [...new Set(blockPoints.map(p => p.price))].sort((a, b) => a - b);
        return (
          <div>
            {uniqueRates.length <= 5 && (
              <div style={{ marginBottom: 8 }}>
                {uniqueRates.map((rate, i) => (
                  <Tag key={i} style={{ marginBottom: 4 }}>
                    {formatPrice4(rate)} DKK/kWh
                  </Tag>
                ))}
              </div>
            )}
            <Table
              dataSource={blockPoints}
              columns={[
                { title: 'TIME', dataIndex: 'timestamp', key: 'hour', width: 60,
                  render: (v: string) => {
                    const dkHour = new Date(v).toLocaleTimeString('da-DK', { timeZone: 'Europe/Copenhagen', hour: '2-digit', minute: '2-digit', hour12: false });
                    return <Text className="tnum" strong>{dkHour}</Text>;
                  }},
                { title: 'DKK/kWh', dataIndex: 'price', key: 'price', align: 'right' as const,
                  render: (v: number) => <Text className="tnum" strong>{formatPrice4(v)}</Text> },
                { title: '', key: 'bar', width: 200,
                  render: (_: unknown, row: { price: number }) => {
                    const pct = globalMax > 0 ? (row.price / globalMax) * 100 : 0;
                    return (
                      <div style={{ background: '#f0fdfa', borderRadius: 4, height: 16, width: '100%' }}>
                        <div style={{ background: '#0d9488', borderRadius: 4, height: 16, width: `${pct}%`, minWidth: pct > 0 ? 2 : 0 }} />
                      </div>
                    );
                  }},
              ]}
              rowKey="timestamp"
              size="small"
              pagination={false}
            />
          </div>
        );
      };

      return (
        <div style={{ padding: '4px 0' }}>
          <div style={{ marginBottom: 10 }}>
            <Tag color="geekblue">Daglig skabelon</Tag>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {blocks.length} {blocks.length === 1 ? 'periode' : 'perioder'} · {detail.totalPricePoints} prispunkter i alt
              {' · Gældende periode markeret for '}{selectedDate.format('D. MMM YYYY')}
            </Text>
          </div>
          <Collapse
            bordered={false}
            defaultActiveKey={[String(activeBlockIdx)]}
            expandIcon={({ isActive }) => <RightOutlined rotate={isActive ? 90 : 0} style={{ fontSize: 10, color: '#94a3b8' }} />}
            style={{ background: 'transparent' }}
            items={blocks.map((block, idx) => {
              const nextBlock = blocks[idx + 1];
              const isActive = idx === activeBlockIdx;
              const uniqueRates = [...new Set(block.points.map(p => p.price))].sort((a, b) => a - b);
              const rateRange = uniqueRates.length === 1
                ? formatPrice4(uniqueRates[0])
                : `${formatPrice4(uniqueRates[0])} – ${formatPrice4(uniqueRates[uniqueRates.length - 1])}`;

              return {
                key: String(idx),
                style: {
                  borderRadius: 8,
                  marginBottom: 4,
                  border: isActive ? '1.5px solid #5eead4' : '1px solid #e5e7eb',
                  background: isActive ? '#f0fdfa' : '#fff',
                  overflow: 'hidden',
                },
                label: (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%' }}>
                    {isActive && <Tag color="teal" style={{ margin: 0 }}>Aktiv</Tag>}
                    <Text strong={isActive} style={{ fontSize: 13 }}>
                      {formatDate(block.startDate)}
                      {nextBlock ? ` → ${formatDate(nextBlock.startDate)}` : ' → nu'}
                    </Text>
                    <div style={{ flex: 1 }} />
                    <Text className="tnum" type="secondary" style={{ fontSize: 12 }}>
                      {rateRange} DKK/kWh
                    </Text>
                    <Text className="tnum" type="secondary" style={{ fontSize: 11 }}>
                      {block.points.length} timer
                    </Text>
                  </div>
                ),
                children: renderTemplateBlock(block),
              };
            })}
          />
        </div>
      );
    }

    // Non-template (step function): use shared table
    const sortedPoints = [...detail.pricePoints].sort((a, b) => a.timestamp.localeCompare(b.timestamp));
    const mapped = sortedPoints.map((pp, idx) => ({ key: `${idx}`, date: pp.timestamp, price: pp.price }));
    const activeIdx = findActiveIndex(sortedPoints.map(p => p.timestamp));

    return renderStepFunctionTable(mapped, activeIdx, `${detail.totalPricePoints} prispunkter i alt`);
  };

  // ─── Spot price columns ───
  const spotColumns = [
    {
      title: 'DK-TID', dataIndex: 'hourUtc', key: 'dk', width: 60,
      render: (v: string) => <Text className="tnum" strong style={{ fontSize: 13 }}>{formatTime(v)}</Text>,
    },
    {
      title: 'UTC', dataIndex: 'hourUtc', key: 'utc', width: 60,
      render: (v: string) => <Text className="tnum" type="secondary" style={{ fontSize: 12 }}>{formatTimeUtc(v)}</Text>,
    },
    {
      title: <><Tag color="orange" style={{ marginRight: 0 }}>DK1</Tag> <Text type="secondary" style={{ fontSize: 11 }}>DKK/kWh</Text></>,
      dataIndex: 'dk1', key: 'dk1', align: 'right' as const,
      render: (v: number | null) => v !== null
        ? <Text className="tnum" strong>{formatPrice4(v)}</Text>
        : <Text type="secondary">—</Text>,
    },
    {
      title: <><Tag color="blue" style={{ marginRight: 0 }}>DK2</Tag> <Text type="secondary" style={{ fontSize: 11 }}>DKK/kWh</Text></>,
      dataIndex: 'dk2', key: 'dk2', align: 'right' as const,
      render: (v: number | null) => v !== null
        ? <Text className="tnum" strong>{formatPrice4(v)}</Text>
        : <Text type="secondary">—</Text>,
    },
  ];

  // ─── Group margins by product ───
  const marginsByProduct = marginRows.reduce<Record<string, { productName: string; pricingModel: string; rates: MarginRow[] }>>((acc, m) => {
    if (!acc[m.supplierProductId]) {
      acc[m.supplierProductId] = { productName: m.productName, pricingModel: m.pricingModel, rates: [] };
    }
    acc[m.supplierProductId].rates.push(m);
    return acc;
  }, {});

  const marginProducts = Object.entries(marginsByProduct)
    .sort(([, a], [, b]) => a.productName.localeCompare(b.productName));

  // ─── Group DataHub prices by owner GLN ───
  const datahubByOwner = datahubPrices.reduce<Record<string, { prices: PriceSummary[] }>>((acc, p) => {
    if (!acc[p.ownerGln]) acc[p.ownerGln] = { prices: [] };
    acc[p.ownerGln].prices.push(p);
    return acc;
  }, {});

  const datahubOwners = Object.entries(datahubByOwner).sort(([a], [b]) => a.localeCompare(b));

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <Row align="middle" justify="space-between">
        <Col>
          <Space align="center" size={12}>
            <DollarOutlined style={{ fontSize: 24, color: '#0d9488' }} />
            <div>
              <Title level={3} style={{ margin: 0 }}>Priser</Title>
              <Text type="secondary">Spotpriser, DataHub-priser og leverandørpriser</Text>
            </div>
          </Space>
        </Col>
      </Row>

      {/* Stats */}
      <Row gutter={16} style={{ marginTop: -8 }}>
        {[
          { title: 'Spotpriser', value: spotLatest?.totalRecords ?? 0, icon: <AreaChartOutlined />, color: '#f59e0b' },
          { title: 'DataHub-priser', value: datahubPrices.length, icon: <ThunderboltOutlined />, color: '#0d9488' },
          { title: 'Leverandørmargin', value: marginProducts.length, icon: <BankOutlined />, color: '#7c3aed' },
          { title: 'Priser i alt', value: prices.length + marginRows.length, icon: <DollarOutlined />, color: '#5d7a91' },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card size="small" style={{ borderRadius: 10, textAlign: 'center' }}>
              <Statistic
                title={s.title} value={s.value}
                prefix={<span style={{ color: s.color }}>{s.icon}</span>}
                styles={{ content: { color: s.color, fontSize: 24 } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Date selector + Tabs */}
      <Card style={{ borderRadius: 12 }}>
        <div style={{
          background: 'linear-gradient(135deg, #f0fdfa, #ccfbf1)',
          border: '2px solid #5eead4',
          borderRadius: 12,
          padding: '16px 24px',
          marginBottom: 20,
          boxShadow: '0 2px 8px rgba(13, 148, 136, 0.12)',
          display: 'flex',
          alignItems: 'center',
          gap: 14,
        }}>
          <CalendarOutlined style={{ fontSize: 28, color: '#0d9488' }} />
          <DatePicker
            value={selectedDate}
            onChange={(d) => d && setSelectedDate(d)}
            format="D. MMMM YYYY"
            allowClear={false}
            variant="borderless"
            style={{ width: 280, fontWeight: 700, fontSize: 24, padding: 0 }}
          />
        </div>

        <Tabs
          defaultActiveKey="margins"
          items={[
            /* ── Supplier margin tab ── */
            {
              key: 'margins',
              label: <Space size={6}><BankOutlined /><span>Leverandørmargin ({marginProducts.length})</span></Space>,
              children: marginProducts.length > 0 ? (
                <Collapse
                  bordered={false}
                  expandIcon={({ isActive }) => <RightOutlined rotate={isActive ? 90 : 0} style={{ fontSize: 11, color: '#94a3b8' }} />}
                  style={{ background: 'transparent' }}
                  items={marginProducts.map(([productId, product]) => {
                    const sortedRates = [...product.rates].sort((a, b) =>
                      new Date(a.validFrom).getTime() - new Date(b.validFrom).getTime());
                    const mapped = sortedRates.map((r, idx) => ({ key: r.id || `${idx}`, date: r.validFrom, price: r.priceDkkPerKwh }));
                    const activeIdx = findActiveIndex(sortedRates.map(r => r.validFrom));
                    const currentRate = activeIdx >= 0 ? sortedRates[activeIdx].priceDkkPerKwh : sortedRates[sortedRates.length - 1]?.priceDkkPerKwh;
                    const modelTag = product.pricingModel === 'SpotAddon'
                      ? { label: 'Spot + tillæg', color: 'orange' }
                      : product.pricingModel === 'Fixed'
                      ? { label: 'Fast pris', color: 'blue' }
                      : { label: product.pricingModel, color: 'default' };

                    return {
                      key: productId,
                      style: panelCardStyle,
                      label: renderPanelHeader(
                        modelTag,
                        product.productName,
                        undefined,
                        sortedRates.length,
                        sortedRates.length === 1 ? 'sats' : 'satser',
                        currentRate,
                      ),
                      children: renderStepFunctionTable(mapped, activeIdx),
                    };
                  })}
                />
              ) : (
                <Empty description="Ingen leverandørmarginer oprettet" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },

            /* ── DataHub tab ── */
            {
              key: 'datahub',
              label: <Space size={6}><ThunderboltOutlined /><span>DataHub-priser ({datahubPrices.length})</span></Space>,
              children: datahubPrices.length > 0 ? (
                <Space direction="vertical" size={16} style={{ width: '100%' }}>
                  {datahubOwners.map(([ownerGln, group]) => (
                    <div key={ownerGln}>
                      <Text type="secondary" style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: 1, fontWeight: 600, display: 'block', marginBottom: 8 }}>
                        Netvirksomhed · <Text className="mono" style={{ fontSize: 11 }} type="secondary">{ownerGln}</Text>
                      </Text>
                      <Collapse
                        bordered={false}
                        expandIcon={({ isActive }) => <RightOutlined rotate={isActive ? 90 : 0} style={{ fontSize: 11, color: '#94a3b8' }} />}
                        style={{ background: 'transparent' }}
                        onChange={(keys) => {
                          // Lazy-load details for expanded panels
                          const keyArr = Array.isArray(keys) ? keys : [keys];
                          keyArr.forEach(k => loadDetail(k as string));
                        }}
                        items={group.prices.map(price => {
                          const detail = detailCache[price.id];
                          // Find current rate from cached detail (if loaded)
                          let currentRate: number | undefined;
                          if (detail) {
                            const sorted = [...detail.pricePoints].sort((a, b) => a.timestamp.localeCompare(b.timestamp));
                            const idx = findActiveIndex(sorted.map(p => p.timestamp));
                            if (idx >= 0) currentRate = sorted[idx].price;
                          }
                          return {
                            key: price.id,
                            style: panelCardStyle,
                            label: renderPanelHeader(
                              { label: price.type, color: typeColors[price.type] || 'default' },
                              `${price.description}`,
                              undefined,
                              price.pricePointCount,
                              price.priceResolution === 'PT1H' ? 'punkter' : 'satser',
                              currentRate,
                              <Space size={4}>
                                {price.priceResolution && (
                                  <Tag color="geekblue" style={{ margin: 0 }}>
                                    {price.priceResolution === 'PT15M' ? '15 min' : price.priceResolution === 'PT1H' ? 'Time' : price.priceResolution}
                                  </Tag>
                                )}
                                {price.isTax && <Tag color="red" style={{ margin: 0 }}>Afgift</Tag>}
                                {price.vatExempt && <Tag color="gold" style={{ margin: 0 }}>Momsfri</Tag>}
                              </Space>,
                            ),
                            children: renderDatahubDetail(price),
                          };
                        })}
                      />
                    </div>
                  ))}
                </Space>
              ) : (
                <Empty description="Ingen priser modtaget fra DataHub" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },

            /* ── Spot tab ── */
            {
              key: 'spot',
              label: <Space size={6}><AreaChartOutlined /><span>Spotpriser</span></Space>,
              children: (
                <Space direction="vertical" size={16} style={{ width: '100%' }}>
                  {spotLoading ? (
                    <Spin size="small" style={{ display: 'block', margin: '24px auto' }} />
                  ) : spotRows.length > 0 ? (
                    <Table
                      dataSource={spotRows}
                      columns={spotColumns}
                      rowKey="hourUtc"
                      size="small"
                      pagination={false}
                      scroll={{ y: 600 }}
                    />
                  ) : (
                    <Empty
                      description={`Ingen spotpriser for ${selectedDate.format('D. MMM YYYY')}`}
                      image={Empty.PRESENTED_IMAGE_SIMPLE}
                    />
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>
    </Space>
  );
}
