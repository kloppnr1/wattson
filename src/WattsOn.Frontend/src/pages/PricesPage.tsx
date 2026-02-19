import { useEffect, useState, useCallback } from 'react';
import {
  Card, Table, Spin, Alert, Space, Typography, Row, Col, Statistic, Tag,
  Tabs, Empty, DatePicker,
} from 'antd';
import {
  DollarOutlined, ThunderboltOutlined, BankOutlined,
  AreaChartOutlined, CalendarOutlined,
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

export default function PricesPage() {
  const [prices, setPrices] = useState<PriceSummary[]>([]);
  const [ourGlns, setOurGlns] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedDetails, setExpandedDetails] = useState<Record<string, PriceDetail>>({});
  const [expandLoading, setExpandLoading] = useState<Record<string, boolean>>({});

  // Shared date selector
  const [selectedDate, setSelectedDate] = useState<Dayjs>(dayjs());

  // Spot prices
  const [spotLatest, setSpotLatest] = useState<SpotLatest | null>(null);
  const [spotRows, setSpotRows] = useState<SpotRow[]>([]);
  const [spotLoading, setSpotLoading] = useState(false);

  // Initial load
  useEffect(() => {
    Promise.all([
      getPrices(),
      getSupplierIdentities(),
      api.get<SpotLatest>('/prices/spot/latest'),
    ])
      .then(([pricesRes, identitiesRes, latestRes]) => {
        setPrices(pricesRes.data);
        setOurGlns(new Set(identitiesRes.data.map(si => si.gln)));
        setSpotLatest(latestRes.data);
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  // Fetch spot prices when date changes
  const fetchSpotForDate = useCallback(async (date: Dayjs) => {
    setSpotLoading(true);
    try {
      const res = await api.get<{ totalRecords: number; rows: SpotRow[] }>(`/prices/spot?date=${date.format('YYYY-MM-DD')}`);
      setSpotRows(res.data.rows);
    } catch { setSpotRows([]); }
    finally { setSpotLoading(false); }
  }, []);

  useEffect(() => { fetchSpotForDate(selectedDate); }, [selectedDate, fetchSpotForDate]);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message="Kunne ikke hente priser" description={error} />;

  // Filter out spot prices from the general list (they have their own tab)
  const isSpot = (p: PriceSummary) => p.chargeId.startsWith('SPOT-');
  const nonSpotPrices = prices.filter(p => !isSpot(p));

  // Supplier prices = owned by our GLN(s); DataHub prices = owned by external parties
  const supplierPrices = nonSpotPrices.filter(p => ourGlns.has(p.ownerGln));
  const datahubPrices = nonSpotPrices.filter(p => !ourGlns.has(p.ownerGln));

  const handleExpand = async (expanded: boolean, record: PriceSummary) => {
    if (!expanded || expandedDetails[record.id]) return;
    setExpandLoading(prev => ({ ...prev, [record.id]: true }));
    try {
      const res = await getPrice(record.id);
      setExpandedDetails(prev => ({ ...prev, [record.id]: res.data }));
    } catch { /* silently fail */ }
    finally { setExpandLoading(prev => ({ ...prev, [record.id]: false })); }
  };

  const priceColumns = [
    {
      title: 'TYPE',
      dataIndex: 'type',
      key: 'type',
      width: 120,
      render: (type: string) => <Tag color={typeColors[type] || 'default'}>{type}</Tag>,
    },
    {
      title: 'CHARGE ID',
      dataIndex: 'chargeId',
      key: 'chargeId',
      render: (v: string) => <Text className="mono">{v}</Text>,
    },
    {
      title: 'BESKRIVELSE',
      dataIndex: 'description',
      key: 'description',
      ellipsis: { showTitle: true },
    },
    {
      title: 'GYLDIGHED',
      key: 'validity',
      render: (_: unknown, record: PriceSummary) => (
        <Text className="tnum">
          {formatDate(record.validFrom)} — {record.validTo ? formatDate(record.validTo) : '→'}
        </Text>
      ),
    },
    {
      title: 'PRISPUNKTER',
      dataIndex: 'pricePointCount',
      key: 'pricePointCount',
      align: 'right' as const,
      render: (v: number) => <Text className="tnum">{v}</Text>,
    },
    {
      title: 'FLAG',
      key: 'flags',
      render: (_: unknown, record: PriceSummary) => (
        <Space size={4}>
          {record.isTax && <Tag color="red">Moms</Tag>}
          {record.vatExempt && <Tag color="gold">Momsfri</Tag>}
          {record.isPassThrough && <Tag color="blue">Viderefakturering</Tag>}
        </Space>
      ),
    },
  ];

  // Expanded row: filter price points by selected date
  const expandedRowRender = (record: PriceSummary) => {
    const detail = expandedDetails[record.id];
    if (expandLoading[record.id]) return <Spin size="small" style={{ margin: 16 }} />;
    if (!detail) return <Text type="secondary">Ingen detaildata</Text>;

    // Filter price points to the selected date (using Danish date from UTC)
    const dateStr = selectedDate.format('YYYY-MM-DD');
    const filtered = detail.pricePoints.filter(pp => {
      const dkDate = new Date(pp.timestamp).toLocaleDateString('sv-SE', { timeZone: 'Europe/Copenhagen' });
      return dkDate === dateStr;
    });

    const displayPoints = filtered.length > 0 ? filtered : detail.pricePoints.slice(0, 48);
    const isFiltered = filtered.length > 0;

    return (
      <div style={{ padding: '8px 0' }}>
        <Text strong style={{ marginBottom: 8, display: 'block' }}>
          {isFiltered
            ? `Prispunkter for ${selectedDate.format('D. MMM YYYY')} (${filtered.length} af ${detail.totalPricePoints})`
            : `Seneste prispunkter (${detail.totalPricePoints} i alt)`}
          {detail.priceResolution && (
            <Tag color="geekblue" style={{ marginLeft: 8 }}>
              {detail.priceResolution === 'PT15M' ? '15 min' : detail.priceResolution === 'PT1H' ? 'Time' : detail.priceResolution}
            </Tag>
          )}
        </Text>
        <Table
          dataSource={displayPoints}
          columns={[
            { title: 'DK-TID', dataIndex: 'timestamp', key: 'dk', width: 70,
              render: (v: string) => <Text className="tnum" strong>{formatTime(v)}</Text> },
            { title: 'UTC', dataIndex: 'timestamp', key: 'utc', width: 70,
              render: (v: string) => <Text className="tnum" type="secondary">{formatTimeUtc(v)}</Text> },
            { title: 'DKK/kWh', dataIndex: 'price', key: 'price', align: 'right' as const,
              render: (v: number) => <Text className="tnum" strong>{formatPrice4(v)}</Text> },
          ]}
          rowKey="timestamp"
          size="small"
          pagination={false}
        />
      </div>
    );
  };

  // Spot price columns: DK1 + DK2 side by side
  const spotColumns = [
    {
      title: 'DK-TID',
      dataIndex: 'hourUtc',
      key: 'dk',
      width: 60,
      render: (v: string) => <Text className="tnum" strong style={{ fontSize: 13 }}>{formatTime(v)}</Text>,
    },
    {
      title: 'UTC',
      dataIndex: 'hourUtc',
      key: 'utc',
      width: 60,
      render: (v: string) => <Text className="tnum" type="secondary" style={{ fontSize: 12 }}>{formatTimeUtc(v)}</Text>,
    },
    {
      title: <><Tag color="orange" style={{ marginRight: 0 }}>DK1</Tag> <Text type="secondary" style={{ fontSize: 11 }}>DKK/kWh</Text></>,
      dataIndex: 'dk1',
      key: 'dk1',
      align: 'right' as const,
      render: (v: number | null) => v !== null
        ? <Text className="tnum" strong>{formatPrice4(v)}</Text>
        : <Text type="secondary">—</Text>,
    },
    {
      title: <><Tag color="blue" style={{ marginRight: 0 }}>DK2</Tag> <Text type="secondary" style={{ fontSize: 11 }}>DKK/kWh</Text></>,
      dataIndex: 'dk2',
      key: 'dk2',
      align: 'right' as const,
      render: (v: number | null) => v !== null
        ? <Text className="tnum" strong>{formatPrice4(v)}</Text>
        : <Text type="secondary">—</Text>,
    },
  ];

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
          { title: 'Leverandørpriser', value: supplierPrices.length, icon: <BankOutlined />, color: '#7c3aed' },
          { title: 'Priser i alt', value: prices.length, icon: <DollarOutlined />, color: '#5d7a91' },
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
          defaultActiveKey="spot"
          items={[
            {
              key: 'spot',
              label: (
                <Space size={6}>
                  <AreaChartOutlined />
                  <span>Spotpriser</span>
                </Space>
              ),
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
            {
              key: 'datahub',
              label: (
                <Space size={6}>
                  <ThunderboltOutlined />
                  <span>DataHub-priser ({datahubPrices.length})</span>
                </Space>
              ),
              children: datahubPrices.length > 0 ? (
                <Table
                  dataSource={datahubPrices}
                  columns={[
                    ...priceColumns.slice(0, 3),
                    {
                      title: 'EJER GLN',
                      dataIndex: 'ownerGln',
                      key: 'ownerGln',
                      render: (v: string) => <Text className="mono" style={{ fontSize: 12 }}>{v}</Text>,
                    },
                    {
                      title: 'OPLØSNING',
                      dataIndex: 'priceResolution',
                      key: 'priceResolution',
                      width: 100,
                      render: (v: string | null) => v ? (
                        <Tag color="geekblue">{v === 'PT15M' ? '15 min' : v === 'PT1H' ? 'Time' : v === 'P1D' ? 'Dag' : v === 'P1M' ? 'Måned' : v}</Tag>
                      ) : <Text type="secondary">—</Text>,
                    },
                    ...priceColumns.slice(3),
                  ]}
                  rowKey="id"
                  pagination={false}
                  size="small"
                  expandable={{ expandedRowRender, onExpand: handleExpand }}
                />
              ) : (
                <Empty description="Ingen priser modtaget fra DataHub" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
            {
              key: 'supplier',
              label: (
                <Space size={6}>
                  <BankOutlined />
                  <span>Leverandørpriser ({supplierPrices.length})</span>
                </Space>
              ),
              children: supplierPrices.length > 0 ? (
                <Table
                  dataSource={supplierPrices}
                  columns={priceColumns}
                  rowKey="id"
                  pagination={false}
                  size="small"
                  expandable={{ expandedRowRender, onExpand: handleExpand }}
                />
              ) : (
                <Empty description="Ingen leverandørpriser oprettet" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              ),
            },
          ]}
        />
      </Card>
    </Space>
  );
}
