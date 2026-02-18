import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography, Button } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import type { MålepunktDetail } from '../api/client';
import { getMålepunkt } from '../api/client';

export default function MålepunktDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [mp, setMp] = useState<MålepunktDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (!id) return;
    getMålepunkt(id)
      .then((res) => setMp(res.data))
      .catch((err) => setError(err.response?.status === 404 ? 'Målepunkt ikke fundet' : err.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (error) return <Alert type="error" message={error} />;
  if (!mp) return null;

  const connectionStateColors: Record<string, string> = {
    Tilsluttet: 'green', Afbrudt: 'red', Ny: 'blue', Nedlagt: 'default',
  };

  const leveranceColumns = [
    {
      title: 'Kunde',
      dataIndex: 'kundeNavn',
      key: 'kundeNavn',
      render: (name: string, record: any) => (
        <a onClick={() => navigate(`/kunder/${record.kundeId}`)}>{name}</a>
      ),
    },
    {
      title: 'Fra',
      dataIndex: 'supplyStart',
      key: 'supplyStart',
      render: (d: string) => new Date(d).toLocaleDateString('da-DK'),
    },
    {
      title: 'Til',
      dataIndex: 'supplyEnd',
      key: 'supplyEnd',
      render: (d: string | null) => d ? new Date(d).toLocaleDateString('da-DK') : '→',
    },
    {
      title: 'Status',
      dataIndex: 'isActive',
      key: 'isActive',
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Aktiv' : 'Afsluttet'}</Tag>,
    },
  ];

  const tidsserieColumns = [
    {
      title: 'Periode',
      key: 'period',
      render: (_: any, record: any) =>
        `${new Date(record.periodStart).toLocaleDateString('da-DK')} → ${record.periodEnd ? new Date(record.periodEnd).toLocaleDateString('da-DK') : '→'}`,
    },
    { title: 'Opløsning', dataIndex: 'resolution', key: 'resolution' },
    { title: 'Version', dataIndex: 'version', key: 'version' },
    {
      title: 'Seneste',
      dataIndex: 'isLatest',
      key: 'isLatest',
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Ja' : 'Nej'}</Tag>,
    },
    {
      title: 'Modtaget',
      dataIndex: 'receivedAt',
      key: 'receivedAt',
      render: (d: string) => new Date(d).toLocaleString('da-DK'),
    },
  ];

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/målepunkter')}>Tilbage</Button>

      <Card>
        <Typography.Title level={3} style={{ fontFamily: 'monospace' }}>{mp.gsrn}</Typography.Title>
        <Descriptions column={2} bordered size="small">
          <Descriptions.Item label="Type">{mp.type}</Descriptions.Item>
          <Descriptions.Item label="Art">{mp.art}</Descriptions.Item>
          <Descriptions.Item label="Afregningsmetode">{mp.settlementMethod}</Descriptions.Item>
          <Descriptions.Item label="Opløsning">{mp.resolution}</Descriptions.Item>
          <Descriptions.Item label="Tilstand">
            <Tag color={connectionStateColors[mp.connectionState] || 'default'}>{mp.connectionState}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Aktiv forsyning">
            <Tag color={mp.hasActiveSupply ? 'green' : 'default'}>{mp.hasActiveSupply ? 'Ja' : 'Nej'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Netområde">{mp.gridArea}</Descriptions.Item>
          <Descriptions.Item label="Netvirksomhed GLN">{mp.gridCompanyGln}</Descriptions.Item>
          {mp.address && (
            <Descriptions.Item label="Adresse" span={2}>
              {mp.address.streetName} {mp.address.buildingNumber}
              {mp.address.floor ? `, ${mp.address.floor}.` : ''}
              {mp.address.suite ? ` ${mp.address.suite}` : ''}
              , {mp.address.postCode} {mp.address.cityName}
            </Descriptions.Item>
          )}
          <Descriptions.Item label="Oprettet">
            {new Date(mp.createdAt).toLocaleString('da-DK')}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title={`Leverancer (${mp.leverancer.length})`}>
        <Table dataSource={mp.leverancer} columns={leveranceColumns} rowKey="id" pagination={false} size="small" />
      </Card>

      <Card title={`Tidsserier (${mp.tidsserier.length})`}>
        <Table dataSource={mp.tidsserier} columns={tidsserieColumns} rowKey="id" pagination={false} size="small" />
      </Card>
    </Space>
  );
}
