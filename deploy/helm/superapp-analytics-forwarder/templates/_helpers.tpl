{{- define "superapp.labels" -}}
app.kubernetes.io/part-of: {{ required "experience jest wymagane (ADR-0041): nazwa experience, do której należy komponent" .Values.experience }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end }}

{{/* Kontekst bezpieczeństwa: non-root, read-only root filesystem (zasady architektury §12). */}}
{{- define "app.podSecurityContext" -}}
runAsNonRoot: true
seccompProfile:
  type: RuntimeDefault
{{- end }}

{{- define "app.containerSecurityContext" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop: ["ALL"]
{{- end }}

{{/* Sondy (ADR-0018): startup = baza i migracje, readiness = stan poda, liveness = proces. */}}
{{- define "superapp.probes" -}}
startupProbe:
  httpGet: { path: /health/startup, port: http }
  periodSeconds: 5
  failureThreshold: 60
readinessProbe:
  httpGet: { path: /health/ready, port: http }
  periodSeconds: 5
livenessProbe:
  httpGet: { path: /health/live, port: http }
  periodSeconds: 10
lifecycle:
  preStop:
    sleep:
      seconds: 5
{{- end }}
