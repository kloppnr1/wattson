import { useEffect, useState } from 'react';
import { Table, Tag, Typography, Space } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Målepunkt } from '../api/client';
import { getMålepunkter } from '../api/client';

const connectionStateColors: Record<string, string> = {
  Tilsluttet: 'green',
  Afbrudt: 'red',
  Ny: 'blue',
  Nedlagt: 'default',
};

export default function MålepunkterPage() {
  const [data, setData] = useState<Målepunkt[]>([]);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    getMålepunkter()
      .then((res) => setData(res.data))
      .finally(() => setLoading(false));
  }, []);

  const columns = [
    {
      title: 'GSRN',
      dataIndex: 'gsrn',
      key: 'gsrn',
      render: (gsrn: string, record: Målepunkt) => (
        <a onClick={() => navigate(`/målepunkter/${record.id}`)} style={{ fontFamily: 'monospace' }}>
          {gsrn}
        </a>
      ),
    },
    { title: 'Type', dataIndex: 'type', key: 'type' },
    { title: 'Afregningsmetode', dataIndex: 'settlementMethod', key: 'settlementMethod' },
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
      <Typography.Title level={3}>Målepunkter</Typography.Title>
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
