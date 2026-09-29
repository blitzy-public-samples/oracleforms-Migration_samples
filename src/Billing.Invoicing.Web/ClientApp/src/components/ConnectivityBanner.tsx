/** Shows the persistent Oracle-unavailable banner while visible. */
export default function ConnectivityBanner({ visible }: { visible: boolean }) {
  if (!visible) {
    return null;
  }

  return (
    <div className="connectivity-banner" role="alert">
      Oracle database is unavailable
    </div>
  );
}
