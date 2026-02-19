import { useEffect, useState } from 'react';
import {
  Card, Table, Spin, Alert, Space, Typography, Row, Col, Statistic, Tag,
  Tabs, Empty, Descriptions,
} from 'antd';
import {
  DollarOutlined, ThunderboltOutlined, BankOutlined,
  AreaChartOutlined,
} from '@ant-design/icons';
import type { PriceSummary, PriceDetail } from '../api/client';
import { getPrices, getPrice, getSupplierIdentities } from '../api/client';
import { formatDate } from '../utils/format';
import api from '../api/client';

const { Text, Title } = Typography;

const formatPrice4 = (v: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 4, maximumFractionDigits: 4 }).format(v);
const formatPrice2 = (v: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v);

const typeColors: Record<string, string> = {
  Tarif: 'teal',
  Gebyr: 'orange',
  Abonnement: 'purple',
};

interface SpotPriceRecord {
  hourUtc: string;
  hourDk: string;
  priceArea: string;
  spotPriceDkkPerMwh: number;
  spotPriceEurPerMwh: number;
  spotPriceDkkPerKwh: number;
}

interface SpotLatest {
  totalRecords: number;
  dk1: { hourUtc: string; hourDk: string; spotPriceDkkPerMwh: number; spotPriceDkkPerKwh: number } | null;
  dk2: { hourUtc: string; hourDk: string; spotPriceDkkPerMwh: number; spotPriceDkkPerKwh: number } | null;
}

export default function PricesPage() {
  const [prices, setPrices] = useState<PriceSummary[]>([]);
  const [ourGlns, setOurGlns] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedDetails, setExpandedDetails] = useState<Record<string, PriceDetail>>({});
  const [expandLoading, setExpandLoading] = useState<Record<string, boolean>>({});

  // Spot prices
  const [spotLatest, setSpotLatest] = useState<SpotLatest | null>(null);
  const [spotPrices, setSpotPrices] = useState<SpotPriceRecord[]>([]);
  const [spotLoading, setSpotLoading] = useState(true);
  const [spotArea, setSpotArea] = useState<string>('DK1');

  useEffect(() => {
    Promise.all([
      getPrices(),
      getSupplierIdentities(),
      api.get<SpotLatest>('/spot-prices/latest'),
      api.get<SpotPriceRecord[]>('/spot-prices?days=7'),
    ])
      .then(([pricesRes, identitiesRes, latestRes, spotRes]) => {
        setPrices(pricesRes.data);
        setOurGlns(new Set(identitiesRes.data.map(si => si.gln)));
        setSpotLatest(latestRes.data);
        setSpotPrices(spotRes.data);
      })
      .catch(err => setError(err.message))
      .finally(() => { setLoading(false); setSpotLoading(false); });
  }, []);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message="Kunne ikke hente priser" description={error} />;

  // Supplier prices = owned by our GLN(s); DataHub prices = owned by external parties
  const supplierPrices = prices.filter(p => ourGlns.has(p.ownerGln));
  const datahubPrices = prices.filter(p => !ourGlns.has(p.ownerGln));
  const filteredSpot = spotPrices.filter(s => s.priceArea === spotArea);

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

  const expandedRowRender = (record: PriceSummary) => {
    const detail = expandedDetails[record.id];
    if (expandLoading[record.id]) return <Spin size="small" style={{ margin: 16 }} />;
    if (!detail) return <Text type="secondary">Ingen detaildata</Text>;

    return (
      <div style={{ padding: '8px 0' }}>
        <Text strong style={{ marginBottom: 8, display: 'block' }}>
          Seneste prispunkter ({detail.totalPricePoints} i alt)
          {detail.priceResolution && (
            <Tag color="geekblue" style={{ marginLeft: 8 }}>
              {detail.priceResolution === 'PT15M' ? '15 min' : detail.priceResolution === 'PT1H' ? 'Time' : detail.priceResolution}
            </Tag>
          )}
        </Text>
        <Table
          dataSource={detail.pricePoints.slice(0, 48)}
          columns={[
            { title: 'Tidspunkt', dataIndex: 'timestamp', key: 'timestamp',
              render: (v: string) => new Date(v).toLocaleString('da-DK') },
            { title: 'Pris (DKK/kWh)', dataIndex: 'price', key: 'price', align: 'right' as const,
              render: (v: number) => <Text className="tnum">{formatPrice4(v)}</Text> },
          ]}
          rowKey="timestamp"
          size="small"
          pagination={false}
        />
      </div>
    );
  };

  const spotColumns = [
    {
      title: 'TIME (DK)',
      dataIndex: 'hourDk',
      key: 'hourDk',
      render: (v: string) => <Text className="tnum">{new Date(v).toLocaleString('da-DK', {
        day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
      })}</Text>,
    },
    {
      title: 'DKK/MWH',
      dataIndex: 'spotPriceDkkPerMwh',
      key: 'spotPriceDkkPerMwh',
      align: 'right' as const,
      render: (v: number) => <Text className="tnum">{formatPrice2(v)}</Text>,
    },
    {
      title: 'DKK/KWH',
      dataIndex: 'spotPriceDkkPerKwh',
      key: 'spotPriceDkkPerKwh',
      align: 'right' as const,
      render: (v: number) => (
        <Text className="tnum" strong>{formatPrice4(v)}</Text>
      ),
    },
    {
      title: 'EUR/MWH',
      dataIndex: 'spotPriceEurPerMwh',
      key: 'spotPriceEurPerMwh',
      align: 'right' as const,
      render: (v: number) => <Text className="tnum" type="secondary">{formatPrice2(v)}</Text>,
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
              <Text type="secondary">Leverandørpriser, DataHub-priser og spotpriser</Text>
            </div>
          </Space>
        </Col>
      </Row>

      {/* Stats */}
      <Row gutter={16}>
        {[
          { title: 'Leverandørpriser', value: supplierPrices.length, icon: <BankOutlined />, color: '#7c3aed' },
          { title: 'DataHub-priser', value: datahubPrices.length, icon: <ThunderboltOutlined />, color: '#0d9488' },
          { title: 'Spotpriser', value: spotLatest?.totalRecords ?? 0, icon: <AreaChartOutlined />, color: '#f59e0b' },
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

      {/* Tabs: Leverandør | Regulerede | Spotpriser */}
      <Card style={{ borderRadius: 12 }}>
        <Tabs
          defaultActiveKey="supplier"
          items={[
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
            {
              key: 'regulated',
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
              key: 'spot',
              label: (
                <Space size={6}>
                  <AreaChartOutlined />
                  <span>Spotpriser ({spotLatest?.totalRecords ?? 0})</span>
                </Space>
              ),
              children: (
                <Space direction="vertical" size={16} style={{ width: '100%' }}>
                  {/* Latest spot summary */}
                  {spotLatest && (spotLatest.dk1 || spotLatest.dk2) ? (
                    <Row gutter={16}>
                      {spotLatest.dk1 && (
                        <Col xs={24} sm={12}>
                          <Card size="small" style={{
                            borderRadius: 10,
                            background: 'linear-gradient(135deg, #fefce8, #fef9c3)',
                            border: '1px solid #fde68a',
                          }}>
                            <Descriptions size="small" column={1} colon={false}
                              title={<Space><Tag color="orange">DK1</Tag><Text type="secondary">Vestdanmark</Text></Space>}>
                              <Descriptions.Item label="Seneste pris">
                                <Text strong className="tnum" style={{ fontSize: 16 }}>
                                  {formatPrice4(spotLatest.dk1.spotPriceDkkPerKwh)} DKK/kWh
                                </Text>
                              </Descriptions.Item>
                              <Descriptions.Item label="MWh-pris">
                                <Text className="tnum">{formatPrice2(spotLatest.dk1.spotPriceDkkPerMwh)} DKK/MWh</Text>
                              </Descriptions.Item>
                              <Descriptions.Item label="Tidspunkt">
                                <Text className="tnum" style={{ fontSize: 12 }}>
                                  {new Date(spotLatest.dk1.hourDk).toLocaleString('da-DK')}
                                </Text>
                              </Descriptions.Item>
                            </Descriptions>
                          </Card>
                        </Col>
                      )}
                      {spotLatest.dk2 && (
                        <Col xs={24} sm={12}>
                          <Card size="small" style={{
                            borderRadius: 10,
                            background: 'linear-gradient(135deg, #eff6ff, #dbeafe)',
                            border: '1px solid #bfdbfe',
                          }}>
                            <Descriptions size="small" column={1} colon={false}
                              title={<Space><Tag color="blue">DK2</Tag><Text type="secondary">Østdanmark</Text></Space>}>
                              <Descriptions.Item label="Seneste pris">
                                <Text strong className="tnum" style={{ fontSize: 16 }}>
                                  {formatPrice4(spotLatest.dk2.spotPriceDkkPerKwh)} DKK/kWh
                                </Text>
                              </Descriptions.Item>
                              <Descriptions.Item label="MWh-pris">
                                <Text className="tnum">{formatPrice2(spotLatest.dk2.spotPriceDkkPerMwh)} DKK/MWh</Text>
                              </Descriptions.Item>
                              <Descriptions.Item label="Tidspunkt">
                                <Text className="tnum" style={{ fontSize: 12 }}>
                                  {new Date(spotLatest.dk2.hourDk).toLocaleString('da-DK')}
                                </Text>
                              </Descriptions.Item>
                            </Descriptions>
                          </Card>
                        </Col>
                      )}
                    </Row>
                  ) : (
                    <Alert
                      type="info"
                      showIcon
                      message="Ingen spotpriser tilgængelige"
                      description="SpotPriceWorker henter automatisk priser fra Energi Data Service (energidataservice.dk). Priser vises her når de er tilgængelige."
                    />
                  )}

                  {/* Spot price table */}
                  {filteredSpot.length > 0 ? (
                    <>
                      <Space>
                        <Text type="secondary">Prisområde:</Text>
                        <Tag
                          color={spotArea === 'DK1' ? 'orange' : 'default'}
                          style={{ cursor: 'pointer' }}
                          onClick={() => setSpotArea('DK1')}
                        >DK1</Tag>
                        <Tag
                          color={spotArea === 'DK2' ? 'blue' : 'default'}
                          style={{ cursor: 'pointer' }}
                          onClick={() => setSpotArea('DK2')}
                        >DK2</Tag>
                      </Space>
                      <Table
                        dataSource={filteredSpot}
                        columns={spotColumns}
                        rowKey="hourUtc"
                        size="small"
                        pagination={filteredSpot.length > 48 ? { pageSize: 48 } : false}
                      />
                    </>
                  ) : !spotLoading && (spotLatest?.totalRecords ?? 0) === 0 ? null : (
                    <Empty description="Ingen spotpriser for valgt område" image={Empty.PRESENTED_IMAGE_SIMPLE} />
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
