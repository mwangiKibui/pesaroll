import { Button } from '../components/Button'

/**
 * The foot of a setup step: "Proceed to <next step>", outlined, on the right. Nothing is saved here;
 * each step saves where the change is made (its drawers, modals and confirmations).
 */
export function Proceed({ label, onProceed }: { label: string; onProceed: () => void }) {
  return (
    <div className="flex justify-end">
      <Button variant="secondary" onClick={onProceed}>
        {label}
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6" /></svg>
      </Button>
    </div>
  )
}
