/**
 * Shared formatting utilities.
 * ALL timestamps are stored as UTC. Display conversion uses Europe/Copenhagen
 * explicitly — never relies on the browser's local timezone.
 */

const TZ = 'Europe/Copenhagen';

/** Format a UTC date string to Danish date (e.g. "1.2.2026") */
export const formatDate = (d: string) =>
  new Date(d).toLocaleDateString('da-DK', { timeZone: TZ });

/** Format a UTC datetime string to Danish datetime (e.g. "1.2.2026 14.30.00") */
export const formatDateTime = (d: string) =>
  new Date(d).toLocaleString('da-DK', { timeZone: TZ });

/** Format a UTC datetime to Danish time only (e.g. "14.30") */
export const formatTime = (d: string) =>
  new Date(d).toLocaleTimeString('da-DK', { timeZone: TZ, hour: '2-digit', minute: '2-digit' });

/** Format a UTC datetime to Danish time with seconds (e.g. "14.30.00") */
export const formatTimeFull = (d: string) =>
  new Date(d).toLocaleTimeString('da-DK', { timeZone: TZ });

/** Format a UTC datetime to UTC time only (e.g. "13:30") */
export const formatTimeUtc = (d: string) => {
  const dt = new Date(d);
  return dt.toISOString().slice(11, 16);
};

/** Format a UTC datetime to UTC date+time (e.g. "19. 13:30") for compact display */
export const formatTimeUtcWithDay = (d: string) => {
  const dt = new Date(d);
  return { day: dt.getUTCDate(), hm: dt.toISOString().slice(11, 16) };
};

/**
 * Format an exclusive period end date as the last day (inclusive).
 * DataHub periods use exclusive end boundaries (e.g. Mar 1 00:00 = "through Feb 28").
 * Subtracts one day so users see the last day of the period.
 */
export const formatPeriodEnd = (d: string) => {
  const date = new Date(d);
  date.setDate(date.getDate() - 1);
  return date.toLocaleDateString('da-DK', { timeZone: TZ });
};

/** Format a number as DKK currency */
export const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { style: 'currency', currency: 'DKK' }).format(amount);

/** Format price with 4 decimal places (DKK/kWh) */
export const formatPrice4 = (v: number) =>
  new Intl.NumberFormat('da-DK', { minimumFractionDigits: 4, maximumFractionDigits: 4 }).format(v);
