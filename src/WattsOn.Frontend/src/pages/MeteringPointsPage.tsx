import { useEffect, useState } from 'react';
import { Table, Tag, Typography, Space } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { MeteringPoint } from '../api/client';
import { getMeteringPoints } from '../api/client';

const connectionStateColors: Record<string, string> = {
  Tilsluttet: 'green',
  Afbrudt: 'red',
  Ny: 'blue',
  Nedlagt: 'default',
};

export default function MeteringPointsPage() {
  const [data, setData] = useState<MeteringPoint[]>([]);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    getMeteringPoints()
      .then((res) => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  const columns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: MeteringPoint) => (
        <a onClick={() => navigate(`/metering-points/${record.id}`)} style={{ fontFamily: 'monospace' }}>
          {gsrn}
        </a>
      ),
    },
    { title: 'Type', dataIndex: 'type', key: 'type' },
    { title: 'Settlementsmetode', dataIndex: 'settlementMethod', key: 'settlementMethod' },
    { title: 'Opløsning', dataIndex: 'resolution', key: 'resolution' },
    {
      title: 'Tilstand',
      dataIndex: 'connectionState',
      key: 'connectionState',
      render: (state: string) => (
        <Tag color={connectionStateColors[state] || 'default'}>{state}</Tag>
      ),
    },
    { title: 'Netområde', dataIndex: 'gridArea', key: 'gridArea' },
    {
      title: 'Aktiv forsyning',
      dataIndex: 'hasActiveSupply',
      key: 'hasActiveSupply',
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>{v ? 'Ja' : 'Nej'}</Tag>
      ),
    },
  ];

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Typography.Title level={3}>MeteringPoints</Typography.Title>
      <Table
        dataSource={data}
        columns={columns}
        rowKey="id"
        loading={loading}
        pagination={{ pageSize: 20 }}
      />
    </Space>
  );
}
