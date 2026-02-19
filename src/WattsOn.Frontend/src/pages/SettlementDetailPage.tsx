import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Card, Descriptions, Table, Tag, Spin, Alert, Space, Typography,
  Button, Row, Col, Divider, Input, Modal, message,
} from 'antd';
import {
  ArrowLeftOutlined, FileTextOutlined, SwapOutlined,
  CheckCircleOutlined, HomeOutlined, LinkOutlined,
  ExclamationCircleOutlined,
} from '@ant-design/icons';
import type { SettlementDocument, SettlementDocumentLine } from '../api/client';
import { getSettlementDocument, confirmSettlement } from '../api/client';

const { Title, Text } = Typography;

import { formatDate, formatDateTime, formatPeriodEnd, formatDKK } from '../utils/format';

const docTypeConfig: Record<string, { label: string; color: string; icon: React.ReactNode }> = {
  settlement: { label: 'Settlement', color: '#5d7a91', icon: <FileTextOutlined /> },
  debitNote: { label: 'Debitnota', color: '#d97706', icon: <SwapOutlined /> },
  creditNote: { label: 'Kreditnota', color: '#059669', icon: <SwapOutlined /> },
};

const statusColors: Record<string, string> = {
  Calculated: 'green', Invoiced: 'blue', Adjusted: 'orange',
};

export default function SettlementDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [doc, setDoc] = useState<SettlementDocument | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmModal, setConfirmModal] = useState(false);
  const [invoiceRef, setInvoiceRef] = useState('');
  const [confirming, setConfirming] = useState(false);
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
        <Space size={4}>
          <Tag color={r.taxCategory === 'S' ? 'blue' : 'default'} style={{ fontSize: 11 }}>
            {r.taxPercent}%
          </Tag>
          <Text className="tnum">{formatDKK(r.taxAmount)}</Text>
        </Space>
      ),
    },
  ];

  return (
    <Space direction="vertical" size={20} style={{ width: '100%' }}>
      <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/settlements')}
        style={{ color: '#7593a9', fontWeight: 500, paddingLeft: 0 }}>
        Settlements
      </Button>

      {/* Document header */}
      <Card style={{ borderRadius: 12 }}>
        <Row gutter={24} align="middle">
          <Col flex="auto">
            <Space size={12} align="center">
              <div style={{
                width: 48, height: 48, borderRadius: 12,
                background: `linear-gradient(135deg, ${config.color}20, ${config.color}10)`,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}>
                <span style={{ fontSize: 20, color: config.color }}>{config.icon}</span>
              </div>
              <div>
                <Title level={3} style={{ margin: 0 }}>{doc.documentId}</Title>
                <Space size={8} style={{ marginTop: 4 }}>
                  <Tag color={config.color} style={{ color: '#fff' }}>{config.label}</Tag>
                  <Tag color={statusColors[doc.status] || 'default'}>{doc.status}</Tag>
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

        {/* Action button */}
        {canConfirm && (
          <>
            <Divider style={{ margin: '20px 0 16px' }} />
            <Button
              type="primary" icon={<CheckCircleOutlined />}
              onClick={() => setConfirmModal(true)}
              style={{ background: '#059669', borderColor: '#059669' }}
            >
              Bekræft fakturering
            </Button>
          </>
        )}

        {doc.externalInvoiceReference && (
          <>
            <Divider style={{ margin: '20px 0 16px' }} />
            <Space>
              <CheckCircleOutlined style={{ color: '#059669' }} />
              <Text>Invoiced som <Text strong>{doc.externalInvoiceReference}</Text></Text>
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
                  Original settlement: <Text strong style={{ color: '#5d7a91' }}>{doc.originalDocumentId}</Text>
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
              <Descriptions.Item label="MeteringPoint">
                <Text className="mono">{doc.meteringPoint.gsrn}</Text>
              </Descriptions.Item>
              <Descriptions.Item label="Prisområde">{doc.meteringPoint.gridArea}</Descriptions.Item>
              <Descriptions.Item label="Calculated">
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
                      Moms ({tax.taxCategory === 'S' ? 'Standard' : 'Fritaget'} {tax.taxPercent}%)
                      {' '}af {formatDKK(tax.taxableAmount)}
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
