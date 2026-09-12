const paths: Record<string, string> = {
  home: "M3 10 12 3l9 7v10H3V10Zm6 10v-7h6v7",
  wallet: "M4 6h15v14H4V6Zm0 0V4h13v2m-2 6h6v5h-6v-5Zm3 2v1",
  campaign: "m3 10 15-6v16L3 14v-4Zm15 0h3v4h-3M6 15l2 6h4l-2-5",
  people:
    "M16 21v-3c0-3-3-5-6-5s-6 2-6 5v3m13-9c3 0 5 2 5 5v4M14 6a4 4 0 1 1-8 0 4 4 0 0 1 8 0Zm3-4a4 4 0 0 1 0 8",
  arrow: "M4 12h16m-6-6 6 6-6 6",
  back: "M20 12H4m6-6-6 6 6 6",
  plus: "M12 4v16M4 12h16",
  check: "m5 12 4 4L20 5",
  close: "m6 6 12 12M6 18 18 6",
  menu: "M4 6h16M4 12h16M4 18h16",
  money: "M3 5h18v14H3V5Zm9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8ZM6 9v6m12-6v6",
  chart: "M4 3v17h17M8 16v-5m5 5V7m5 9V4",
  settings: "M4 7h16M4 17h16M8 4v6m8 4v6",
  bell: "M5 16h14l-2-3V8a5 5 0 0 0-10 0v5l-2 3Zm5 4h4",
  document: "M6 3h9l4 4v14H6V3Zm9 0v5h4M9 12h7m-7 4h7",
  search: "M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14Zm5 12 6 6",
  calendar: "M4 6h16v15H4V6Zm0 5h16M8 3v6m8-6v6",
  lock: "M6 10h12v11H6V10Zm2 0V6a4 4 0 0 1 8 0v4m-4 5v2",
  video: "M3 5h13v14H3V5Zm13 5 5-3v10l-5-3v-4",
  qr: "M3 3h6v6H3V3Zm12 0h6v6h-6V3ZM3 15h6v6H3v-6Zm12 0h3v3h3v3h-6v-6ZM3 12h9V3m0 12v6m6-9h3",
  globe:
    "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM3 12h18M12 3c5 5 5 13 0 18-5-5-5-13 0-18Z",
  location:
    "M19 9c0 5-7 12-7 12S5 14 5 9a7 7 0 0 1 14 0ZM12 6a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z",
  logout: "M9 4H4v16h5m6-14 6 6-6 6m-6-6h12",
  sparkle: "m12 2 3 7 7 3-7 3-3 7-3-7-7-3 7-3 3-7Z",
  info: "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Zm0 7v7m0-11v1",
};
export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name] ?? paths.document} />
    </svg>
  );
}
