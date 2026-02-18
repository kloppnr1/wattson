import { useEffect, useState } from 'react';
import { Card, Table, Spin, Alert, Space, Typography, Row, Col, Statistic, Select, Input, Tag } from 'antd';
import type { PriceSummary, PriceDetail } from '../api/client';
import { getPrices, getPrice } from '../api/client';

const { Text } = Typography;

const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK');
const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 4, maximumFractionDigits: 4 }).format(amount);

const typeColors: Record<string, string> = {
  Tarif: 'teal',
  Gebyr: 'orange',
  Abonnement: 'purple',
};

export default function PricesPage() {
  const [prices, setPrices] = useState<PriceSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [search, setSearch] = useState('');
  const [expandedDetails, setExpandedDetails] = useState<Record<string, PriceDetail>>({});
  const [expandLoading, setExpandLoading] = useState<Record<string, boolean>>({});

  useEffect(() => {
    getPrices()
      .then(res => setPrices(res.data))
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (error) return <Alert type="error" message="Kunne ikke hente priser" description={error} />;

  const filtered = prices.filter(p => {
    if (typeFilter !== 'all' && p.type !== typeFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      if (!p.chargeId.toLowerCase().includes(q) && !p.description.toLowerCase().includes(q)) return false;
    }
    return true;
  });

  const now = new Date();
  const tariffs = prices.filter(p => p.type === 'Tarif').length;
  const active = prices.filter(p => !p.validTo || new Date(p.validTo) > now).length;
  const taxCount = prices.filter(p => p.isTax).length;

  const handleExpand = async (expanded: boolean, record: PriceSummary) => {
    if (!expanded || expandedDetails[record.id]) return;
    setExpandLoading(prev => ({ ...prev, [record.id]: true }));
    try {
      const res = await getPrice(record.id);
      setExpandedDetails(prev => ({ ...prev, [record.id]: res.data }));
    } catch {
      // silently fail, row just won't show detail
    } finally {
      setExpandLoading(prev => ({ ...prev, [record.id]: false }));
    }
  };

  const columns = [
    {
      title: 'TYPE',
      dataIndex: 'type',
      key: 'type',
      width: 130,
      render: (type: string) => {
        const color = typeColors[type] || 'default';
        return <Tag color={color}>{type}</Tag>;
      },
    },
    {
      title: 'CHARGE ID',
      dataIndex: 'chargeId',
      key: 'chargeId',
      render: (v: string) => <Text style={{ fontFamily: 'monospace' }}>{v}</Text>,
    },
    {
      title: 'DESCRIPTION',
      dataIndex: 'description',
      key: 'description',
      width: 220,
      ellipsis: { showTitle: true },
    },
    {
      title: 'OWNER GLN',
      dataIndex: 'ownerGln',
      key: 'ownerGln',
      render: (v: string) => <Text style={{ fontFamily: 'monospace' }}>{v}</Text>,
    },
    {
      title: 'VALIDITY',
      key: 'validity',
      render: (_: unknown, record: PriceSummary) => (
        <Text className="tnum">
          {formatDate(record.validFrom)} — {record.validTo ? formatDate(record.validTo) : '→'}
        </Text>
      ),
    },
    {
      title: 'PRICE POINTS',
      dataIndex: 'pricePointCount',
      key: 'pricePointCount',
      align: 'right' as const,
      render: (v: number) => <Text className="tnum">{v}</Text>,
    },
    {
      title: 'LINKED MPs',
      dataIndex: 'linkedMeteringPoints',
      key: 'linkedMeteringPoints',
      align: 'right' as const,
      render: (v: number) => <Text className="tnum">{v}</Text>,
    },
    {
      title: 'FLAGS',
      key: 'flags',
      render: (_: unknown, record: PriceSummary) => (
        <Space size={4}>
          {record.isTax && <Tag color="red">Tax</Tag>}
          {record.vatExempt && <Tag color="gold">VAT-exempt</Tag>}
          {record.isPassThrough && <Tag color="blue">Pass-through</Tag>}
        </Space>
      ),
    },
  ];

  const expandedRowRender = (record: PriceSummary) => {
    const detail = expandedDetails[record.id];
    if (expandLoading[record.id]) return <Spin size="small" style={{ margin: 16 }} />;
    if (!detail) return <Text type="secondary">No detail data</Text>;

    const pricePointCols = [
      {
        title: 'Timestamp',
        dataIndex: 'timestamp',
        key: 'timestamp',
        render: (v: string) => new Date(v).toLocaleString('da-DK'),
      },
      {
        title: 'Price (DKK/kWh)',
        dataIndex: 'price',
        key: 'price',
        align: 'right' as const,
        render: (v: number) => <Text className="tnum">{formatDKK(v)}</Text>,
      },
    ];

    const linkCols = [
      {
        title: 'GSRN',
        dataIndex: 'gsrn',
        key: 'gsrn',
        render: (v: string) => <span className="gsrn-badge">{v}</span>,
      },
      {
        title: 'Link Period',
        key: 'period',
        render: (_: unknown, link: { linkFrom: string; linkTo: string | null }) => (
          <Text className="tnum">
            {formatDate(link.linkFrom)} — {link.linkTo ? formatDate(link.linkTo) : '→'}
          </Text>
        ),
      },
    ];

    return (
      <Space direction="vertical" size={16} style={{ width: '100%', padding: '8px 0' }}>
        <div>
          <Text strong style={{ marginBottom: 8, display: 'block' }}>
            Recent Price Points ({detail.totalPricePoints} total)
          </Text>
          <Table
            dataSource={detail.pricePoints.slice(0, 20)}
            columns={pricePointCols}
            rowKey="timestamp"
            size="small"
            pagination={false}
          />
        </div>
        <div>
          <Text strong style={{ marginBottom: 8, display: 'block' }}>
            Linked Metering Points ({detail.linkedMeteringPoints.length})
          </Text>
          {detail.linkedMeteringPoints.length > 0 ? (
            <Table
              dataSource={detail.linkedMeteringPoints}
              columns={linkCols}
              rowKey="id"
              size="small"
              pagination={false}
            />
          ) : (
            <Text type="secondary">No linked metering points</Text>
          )}
        </div>
      </Space>
    );
  };

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <Space direction="vertical" size={24} style={{ width: '100%' }}>
      <div className="page-header">
        <h2>Priser &amp; Tariffer</h2>
        <div className="page-subtitle">Tariffer, gebyrer og abonnementer fra DataHub</div>
      </div>

      {/* Stats — 4 cards */}
      <Row gutter={16}>
        {[
          { title: 'Total Prices', value: prices.length },
          { title: 'Tariffs', value: tariffs, color: '#0d9488' },
          { title: 'Active', value: active, color: '#10b981' },
          { title: 'Tax Charges', value: taxCount, color: '#dc2626' },
        ].map(s => (
          <Col xs={12} sm={6} key={s.title}>
            <Card style={{ borderRadius: 8 }}>
              <Statistic
                title={s.title}
                value={s.value}
                styles={{ content: { fontSize: 36, fontWeight: 700, color: s.color || '#1a202c' } }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      {/* Filter bar */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <Select
            value={typeFilter}
            onChange={setTypeFilter}
            style={{ flex: 1 }}
            options={[
              { value: 'all', label: 'All types' },
              { value: 'Tarif', label: 'Tarif' },
              { value: 'Gebyr', label: 'Gebyr' },
              { value: 'Abonnement', label: 'Abonnement' },
            ]}
          />
          <Input
            placeholder="Search ChargeId or description..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            allowClear
            style={{ flex: 2 }}
          />
        </div>
      </Card>

      {/* Table */}
      <Card style={{ borderRadius: 8, padding: 0 }} styles={{ body: { padding: 0 } }}>
        <Table
          dataSource={filtered}
          columns={columns}
          rowKey="id"
          pagination={filtered.length > 20 ? { pageSize: 20 } : false}
          expandable={{
            expandedRowRender,
            onExpand: handleExpand,
          }}
        />
      </Card>
    </Space>
  );
}
