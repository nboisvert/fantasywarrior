import logo from "../assets/logo.webp";

/**
 * Shared loading indicator — a gently breathing app logo, replacing plain
 * "Loading…" text. Reuses the `.empty-state` wrapper spacing so it drops
 * into the same slots those messages used to occupy. `label` is optional:
 * pass contextual copy (e.g. "Loading stats…") when it helps orient the
 * user, or omit it for a bare, quieter indicator.
 */
export function LoadingLogo({ label }: { label?: string }) {
  return (
    <div className="empty-state loading-logo" role="status" aria-live="polite" aria-label={label ?? "Loading"}>
      <img className="loading-logo-img" src={logo} alt="" aria-hidden="true" />
      {label && <p className="loading-logo-label">{label}</p>}
    </div>
  );
}
