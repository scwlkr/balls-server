# Use a limited identity for each access grant

Each client Windows profile receives a separate, non-administrative access identity for the host's managed share. This was chosen over guest access, a single shared credential, or the owner's personal Windows/Microsoft credentials so one grant can be audited, rotated, or revoked without affecting other clients or exposing a high-value sign-in secret. An access grant is a revocable credential identity, not cryptographic proof that a physical device is trustworthy.
