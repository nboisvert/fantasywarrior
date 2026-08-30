// ToastHost — renders whatever arrives on the live channel's `notice` event.
//
// Nothing produces a notice yet, on purpose: the transport, the stack and the
// dismissal rules ship first, so wiring the first real one (a trade offer, a
// week closing) is a single push from the endpoint that already handles the
// action — the same way awarding cockcoin happens inline at the earning action
// rather than in a service of its own.

import { useEffect, useState } from "react";
import { useLive, type LiveNotice } from "../live/LiveProvider";
import { useLanguage } from "../i18n/LanguageContext";
import { XIcon } from "./Icons";
import "./Toast.css";

const DISMISS_MS = 6000;

/** Enough to stack without burying the screen; older ones fall off the bottom. */
const MAX_VISIBLE = 3;

export function ToastHost() {
  const { t } = useLanguage();
  const { onNotice } = useLive();
  const [notices, setNotices] = useState<LiveNotice[]>([]);

  useEffect(
    () =>
      onNotice((notice) => {
        setNotices((prev) => [...prev, notice].slice(-MAX_VISIBLE));
        window.setTimeout(
          () => setNotices((prev) => prev.filter((n) => n.id !== notice.id)),
          DISMISS_MS,
        );
      }),
    [onNotice],
  );

  if (notices.length === 0) return null;

  return (
    // aria-live rather than a dialog: these interrupt nothing and steal no
    // focus, they just have to be announced when they appear.
    <div className="toast-host" role="status" aria-live="polite">
      {notices.map((notice) => (
        <div key={notice.id} className={`toast toast-${notice.kind}`}>
          <span className="toast-text">{notice.text}</span>
          <button
            className="toast-close"
            onClick={() => setNotices((prev) => prev.filter((n) => n.id !== notice.id))}
            aria-label={t("toast.dismiss")}
          >
            <XIcon size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
