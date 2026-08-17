# Separate the unelevated dashboard from privileged setup

Balls Server keeps its dashboard and diagnostics unelevated. System changes run only through a separate, narrowly scoped helper after the user sees a change preview and gives explicit approval. This was chosen over elevating the whole application or hiding setup actions behind dormant flags because it reduces privilege exposure, keeps diagnosis independently usable, and gives privileged operations a testable consent, ownership, and recovery boundary. Installation remains a separate distribution responsibility.
