# Lokalna infrastruktura Kubernetes (Development / Sandbox)

> [!WARNING]
> **NINIEJSZE MANIFESTY SĄ PRZEZNACZONE WYŁĄCZNIE DLA ŚRODOWISKA LOKALNEGO (Docker Desktop, Minikube, Kind).**
> **NIE WDRAŻAĆ NA ŚRODOWISKA TESTOWE, STAGE ANI PRODUKCYJNE.**

---

## Przeznaczenie

Manifesty w tym katalogu stanowią odpowiednik `docker-compose.yml` dla lokalnego klastra Kubernetes. Służą wyłącznie do uruchomienia zależności pomocniczych na maszynie programisty, aby umożliwić lokalny deployment i testowanie chartów Helm aplikacji (`superapp-bff`, `superapp-service`, `superapp-gateway` itp.) bez konieczności podpinania zewnętrznych zasobów chmurowych.

Manifesty zawierają jawne, developerskie dane uwierzytelniające (`Dev!Passw0rd1`, wyłączone reguły haseł) i pojedyncze instancje procesów (brak HA, brak trwałego backupu produkcyjnego).

---

## Architektura na środowiskach wyższych (Test, Stage, Prod)

W architekturze korporacyjnej / chmurowej infrastruktura bazodanowa i brokerska **nie jest stawiana wewnątrz klastra aplikacji z tych manifestów**. Zamiast tego wykorzystywane są:

| Komponent | Lokalnie (ten katalog) | Środowiska Test / Stage / Prod |
| :--- | :--- | :--- |
| **Baza danych (MSSQL)** | Pojedynczy pod `mssql` (SQL Express) | Usługa zarządzana: **Azure SQL Database**, **AWS RDS for SQL Server** lub dedykowany klaster bazodanowy |
| **Cache (Redis)** | Pojedynczy pod `redis` | Usługa zarządzana: **Azure Cache for Redis**, **AWS ElastiCache** |
| **Broker (RabbitMQ)** | Pojedynczy pod `rabbitmq` | Usługa zarządzana: **Azure Service Bus**, **AWS SQS/SNS** lub korporacyjny klaster RabbitMQ |
| **CIAM / IdP (Keycloak)** | Pojedynczy pod `keycloak` | Centralny korporacyjny IdP: **Microsoft Entra ID (B2C)**, **Okta** lub dedykowany klaster CIAM |

### Kontrakt konfiguracji (Secrets)

Charty aplikacji w [`deploy/helm/`](file:///c:/Projekty/project_template/deploy/helm) są całkowicie odcięte od sposobu hostowania infrastruktury. Każdy chart przyjmuje jedynie nazwę sekretu:

```yaml
envFrom:
  - secretRef:
      name: {{ .Values.secretName }}
```

* **Lokalnie**: `secretName` pochodzi z pliku [`05-secrets.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/05-secrets.yaml) i wskazuje na lokalne pody (`Server=mssql,1433`, `redis:6379`, `rabbitmq:5672`).
* **Test / Stage / Prod**: `secretName` jest wstrzykiwany przez np. **External Secrets Operator** (synchronizujący poświadczenia z Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) i wskazuje na właściwe endpointy chmurowe.

---

## Zawartość katalogu

* [`01-mssql.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/01-mssql.yaml) – lokalny MSSQL z automatycznym bootstrapem schematów i uprawnień (`01-bootstrap.sql`).
* [`02-redis.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/02-redis.yaml) – lokalny Redis standalone.
* [`03-rabbitmq.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/03-rabbitmq.yaml) – lokalny RabbitMQ z panelem zarządzania (port 15672).
* [`04-keycloak.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/04-keycloak.yaml) – lokalny Keycloak z prekonfigurowanym realm'em `SuperApp`.
* [`05-secrets.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/05-secrets.yaml) – domyślne lokalne poświadczenia i connection stringi do powyższych podów.
* [`06-migrator-job.yaml`](file:///c:/Projekty/project_template/deploy/local/k8s/06-migrator-job.yaml) – Job K8s aplikujący migracje EF Core na lokalną bazę.

---

## Zarządzanie środowiskiem przez narzędzie CLI (`dotnet superapp env`)

Narzędzie CLI wspiera pełną obsługę lokalnego klastra Kubernetes (flaga `--k8s`):

1. **Uruchomienie środowiska lokalnego na Kubernetesie:**
   ```powershell
   # Całe środowisko (infrastruktura w k8s + migracje + charty Helm aplikacji):
   dotnet superapp env up --k8s

   # Tylko infrastruktura pomocnicza (MSSQL, Redis, RabbitMQ, Keycloak):
   dotnet superapp env up --k8s --infra
   ```

2. **Weryfikacja stanu podów i usług:**
   ```powershell
   dotnet superapp env status --k8s
   ```

3. **Zatrzymanie lokalnego środowiska Kubernetes:**
   ```powershell
   # Zatrzymanie aplikacji i usunięcie zasobów:
   dotnet superapp env down --k8s

   # Reset bazy danych i wolumenów (usunięcie PVC oraz Jobs):
   dotnet superapp env down --k8s --reset
   ```

---

## Generowanie sparametryzowanych tokenów JWT (`dotnet superapp env token`)

Polecenie `dotnet superapp env token` pozwala na generowanie kryptograficznie poprawnych tokenów JWT, które są **akceptowane przez BFF oraz mikroserwisy domenowe**.

### Dlaczego to działa (Zgodność Issuer i Audience):
* **Lokalnie w IDE**: Serwisy oczekują wystawcy `http://localhost:8081/realms/superapp` (`appsettings.Development.json`).
* **W klastrze Kubernetes**: Pody w klastrze mają skonfigurowane `Authentication__Authority: http://keycloak:8080/realms/superapp`.
  Flaga `--k8s` automatycznie nakłada nagłówek `Host: keycloak:8080`, dzięki czemu Keycloak podpisuje token kluczem RS256 z wystawcą `iss: http://keycloak:8080/realms/superapp`.
* **Audiences**: Token automatycznie zawiera audiences dla wszystkich żądanych serwisów (`knowledge-api`, `sleepdiary-api`, `example-bff`, `gateway-mobile`).

### Przykłady użycia:

1. **Token dla środowiska Kubernetes z podglądem claimów (`--decode`):**
   ```powershell
   dotnet superapp env token --k8s --decode
   ```

2. **Format gotowy do curl (`--curl`):**
   ```powershell
   dotnet superapp env token --k8s --curl
   # Zwraca: -H "Authorization: Bearer eyJhbGciOi..."
   ```

3. **Wywołanie API w klastrze przez curl (np. port forward BFF):**
   ```powershell
   $token = dotnet superapp env token --k8s
   curl -H "Authorization: Bearer $token" http://localhost:5120/internal/v1/widgets/sleep-summary
   ```

4. **Wydanie tokenu o zawężonych scope'ach i dla konkretnego użytkownika:**
   ```powershell
   dotnet superapp env token reader --k8s --scope knowledge.catalog.read --decode
   ```

5. **Weryfikacja obecności konkretnego audience (`--audience`):**
   ```powershell
   dotnet superapp env token --k8s --audience knowledge-api --audience example-bff
   ```

6. **Format JSON do automatyzacji skryptowej:**
   ```powershell
   dotnet superapp env token --k8s --json
   ```

---

## Ręczne uruchomienie bez narzędzia CLI

1. **Uruchomienie infrastruktury:**
   ```powershell
   kubectl apply -f deploy/local/k8s/
   ```

2. **Weryfikacja stanu podów:**
   ```powershell
   kubectl get pods -l 'app in (mssql,redis,rabbitmq,keycloak)'
   ```

3. **Usunięcie lokalnej infrastruktury:**
   ```powershell
   kubectl delete -f deploy/local/k8s/
   ```
