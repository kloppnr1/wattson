/** Format a date string to Danish locale (e.g. "1.2.2026") */
export const formatDate = (d: string) => new Date(d).toLocaleDateString('da-DK');

/** Format a datetime string to Danish locale (e.g. "1.2.2026 14.30.00") */
export const formatDateTime = (d: string) => new Date(d).toLocaleString('da-DK');

/**
 * Format an exclusive period end date as the last day (inclusive).
 * DataHub periods use exclusive end boundaries (e.g. Mar 1 00:00 = "through Feb 28").
 * This subtracts one day so users see the last day of the period.
 */
export const formatPeriodEnd = (d: string) => {
  const date = new Date(d);
  date.setDate(date.getDate() - 1);
  return date.toLocaleDateString('da-DK');
};

/** Format a number as DKK currency */
export const formatDKK = (amount: number) =>
  new Intl.NumberFormat('da-DK', { style: 'currency', currency: 'DKK' }).format(amount);
