import type { ThemeConfig } from 'antd';

export const wattsOnTheme: ThemeConfig = {
  token: {
    colorPrimary: '#3d5a6e',
    colorPrimaryHover: '#415b73',
    colorPrimaryActive: '#263545',

    fontFamily: "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
    fontFamilyCode: "'Space Mono', monospace",
    fontSize: 14,

    borderRadius: 8,
    borderRadiusLG: 8,
    borderRadiusSM: 6,
    colorBorder: '#e2e8f0',
    colorBorderSecondary: '#f3f4f6',

    colorBgContainer: '#ffffff',
    colorBgLayout: '#eef1f5',
    colorBgElevated: '#ffffff',

    colorText: '#1a202c',
    colorTextSecondary: '#4b5563',
    colorTextTertiary: '#9ca3af',
    colorTextDescription: '#6b7280',

    boxShadow: 'none',
    boxShadowSecondary: 'none',

    colorLink: '#6d28d9',
    colorLinkHover: '#5b21b6',
    colorLinkActive: '#4c1d95',

    colorSuccess: '#10b981',
    colorError: '#dc2626',
    colorWarning: '#d97706',
    colorInfo: '#3b82f6',
  },
  components: {
    Layout: {
      siderBg: '#3d5a6e',
      headerBg: '#ffffff',
      bodyBg: '#eef1f5',
    },
    Menu: {
      darkItemBg: 'transparent',
      darkItemColor: 'rgba(255, 255, 255, 0.7)',
      darkItemHoverBg: 'rgba(255, 255, 255, 0.12)',
      darkItemSelectedBg: 'rgba(255, 255, 255, 0.15)',
      darkItemSelectedColor: '#ffffff',
      darkSubMenuItemBg: 'transparent',
      darkGroupTitleColor: 'rgba(255, 255, 255, 0.4)',
      itemBorderRadius: 6,
      itemMarginInline: 8,
      groupTitleFontSize: 11,
    },
    Table: {
      headerBg: '#ffffff',
      headerColor: '#9ca3af',
      headerSplitColor: '#f3f4f6',
      borderColor: '#f3f4f6',
      rowHoverBg: '#f9fafb',
      headerBorderRadius: 0,
      cellFontSize: 14,
      cellPaddingBlock: 14,
      cellPaddingInline: 16,
    },
    Card: {
      borderRadiusLG: 8,
      boxShadowTertiary: 'none',
    },
    Tag: {
      borderRadiusSM: 4,
    },
    Statistic: {
      titleFontSize: 14,
      contentFontSize: 36,
    },
    Button: {
      borderRadius: 6,
      controlHeight: 38,
      primaryShadow: 'none',
    },
    Descriptions: {
      labelBg: '#f9fafb',
    },
    Segmented: {
      trackBg: '#eef1f5',
      itemSelectedBg: '#ffffff',
      itemSelectedColor: '#1e293b',
      borderRadiusSM: 8,
    },
    Input: {
      borderRadius: 6,
      controlHeight: 38,
    },
    Select: {
      borderRadius: 6,
      controlHeight: 38,
    },
    DatePicker: {
      borderRadius: 6,
      controlHeight: 38,
    },
  },
};
