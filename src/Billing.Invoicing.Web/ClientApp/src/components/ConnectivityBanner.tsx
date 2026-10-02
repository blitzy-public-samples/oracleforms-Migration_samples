import { useLayoutEffect, useRef } from 'react';

/** Root-element custom property holding the banner's block size. */
const BANNER_SIZE_PROPERTY = '--connectivity-banner-size';

/** Extent in px of the focus ring drawn outside a focused control (2px outline, 1px offset). */
const FOCUS_RING_EXTENT = 3;

/** Text of the banner. */
const BANNER_TEXT = 'Oracle database is unavailable';

/** Shows the persistent Oracle-unavailable banner while visible, publishes its block size on the root element and scrolls a focused control it covers clear of it. */
export default function ConnectivityBanner({ visible }: { visible: boolean }) {
  const sizer = useRef<HTMLDivElement>(null);
  const banner = useRef<HTMLDivElement>(null);

  // Publishes the banner's block size, measured on its hidden twin, on the root element.
  useLayoutEffect(() => {
    const element = sizer.current;
    if (element === null) {
      return undefined;
    }
    const rootStyle = document.documentElement.style;
    const publish = () => {
      rootStyle.setProperty(BANNER_SIZE_PROPERTY, `${element.getBoundingClientRect().height}px`);
    };
    publish();
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(publish) : null;
    observer?.observe(element);
    return () => {
      observer?.disconnect();
      rootStyle.removeProperty(BANNER_SIZE_PROPERTY);
    };
  }, []);

  // When the banner appears over the focused control or its focus ring, scrolls that control clear of it.
  useLayoutEffect(() => {
    const element = banner.current;
    if (!visible || element === null) {
      return;
    }
    const focused = document.activeElement;
    if (!(focused instanceof HTMLElement) || focused === document.body || element.contains(focused)) {
      return;
    }
    const bannerTop = element.getBoundingClientRect().top;
    const control = focused.getBoundingClientRect();
    if (control.bottom + FOCUS_RING_EXTENT > bannerTop && control.top < window.innerHeight) {
      focused.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
  }, [visible]);

  return (
    <>
      <div ref={sizer} className="connectivity-banner-sizer" aria-hidden="true">
        {BANNER_TEXT}
      </div>
      {visible ? (
        <div ref={banner} className="connectivity-banner" role="alert">
          {BANNER_TEXT}
        </div>
      ) : null}
    </>
  );
}
