# Production readiness ve operasyonel dayanıklılık

Bu belge, belirli bir IIS/Windows Service, dosya sistemi, antivirüs, ağ paylaşımı veya cloud sağlayıcısı seçmeden GraduateApp için güvenli çalışma sözleşmesini tanımlar. Buradaki varsayılanlar geliştirme ve doğrulama içindir; kurumsal altyapı kararı yerine geçmez.

## Yapılandırma

Secret olmayan ayarlar `appsettings.json` içinde varsayılan olarak bulunur. Secret değerler source control'e yazılmaz.

| Host | Anahtar | Varsayılan | Geçerli aralık / kural |
| --- | --- | ---: | --- |
| API | `DatabaseTimeouts:ConnectionSeconds` | 5 | 1–30 saniye |
| API | `DatabaseTimeouts:CommandSeconds` | 8 | 2–60 saniye |
| API | `DatabaseTimeouts:ReadinessSeconds` | 3 | 1–10 saniye ve diğer iki SQL timeout'undan kısa |
| Web | `GraduateApi:BaseAddress` | geliştirme HTTPS adresi | Mutlak HTTP(S) URI |
| Web | `GraduateApi:TimeoutSeconds` | 15 | 3–120 saniye |
| Web | `GraduateApi:InnerDependencyTimeoutSeconds` | 8 | 1–60 saniye ve Web timeout'undan kısa |

Geçersiz timeout veya URI ayarı `ValidateOnStart` nedeniyle host başlangıcında reddedilir. Production sağlayıcılarının eksikliği ise host'u düşürmez; readiness unhealthy olur ve belge yükleme fail-closed kalır.

Örnek environment variable adları:

```text
ConnectionStrings__DefaultConnection=<secret store tarafından sağlanır>
DatabaseTimeouts__ConnectionSeconds=5
DatabaseTimeouts__CommandSeconds=8
DatabaseTimeouts__ReadinessSeconds=3
GraduateApi__BaseAddress=https://api.example.invalid/
GraduateApi__TimeoutSeconds=15
GraduateApi__InnerDependencyTimeoutSeconds=8
```

Connection string bir secret'tır. Log, health response, ProblemDetails, dokümantasyon veya deployment çıktısına yazılmamalıdır.

## Timeout bütçesi

İç bağımlılık önce sonlanmalı, dış çağıran ona hata cevabı üretmek için zaman bulmalıdır:

```text
SQL readiness (3 sn) < SQL connection (5 sn) < SQL command / beklenen API iç bütçesi (8 sn) < Web→API (15 sn)
```

`GraduateApi:InnerDependencyTimeoutSeconds`, deploy edilen API'nin en uzun SQL request bütçesiyle aynı tutulmalıdır. Web timeout'u bundan kesinlikle büyük olmalıdır. Readiness kendi kısa linked cancellation token'ını kullanır. Global `EnableRetryOnFailure`, özel retry döngüsü veya otomatik transaction tekrarı yoktur; explicit transaction, audit ve optimistic concurrency semantiği değişmez.

## Exception ve HTTP sözleşmesi

| Durum | API sonucu | Not |
| --- | --- | --- |
| SQL timeout (`-2`) veya doğrulanmış bağlantı erişilemezliği | 503 | Genel Türkçe ProblemDetails; altyapı ayrıntısı yok |
| Authentication sırasında SQL erişilemezliği | 503 | Kullanıcı/token hatası olmadığı için 401 değildir |
| SQL deadlock `1205` | Mevcut servis sınırlarında 409 | Mevcut kontrollü concurrency davranışı korunur |
| Unique violation `2601` / `2627` | Mevcut servis sınırlarında 409 | Duplicate iş kuralı davranışı korunur |
| Caller/request cancellation | 499, boş cevap | 401/409/500/503'e çevrilmez |
| Beklenmeyen exception | 500 | Güvenli genel ProblemDetails ve exception type içeren PII-free event |
| Web transport timeout / API'ye erişilememe | 503 | Cookie kapatılmaz |

503 kullanıcı mesajı:

> Servis geçici olarak kullanılamıyor. Lütfen kısa bir süre sonra tekrar deneyin.

SQL, connection string, data source, database adı, stack trace veya token response'a eklenmez. Response başladıktan sonra status değiştirilmeye çalışılmaz.

## Health endpoint'leri

API iki anonymous endpoint sunar:

- `GET /health/live`: yalnız process/pipeline çalışmasını bildirir. SQL, storage, scanner veya Data Protection probe'u çağırmaz.
- `GET /health/ready`: yeni bir `SqlConnection` açmayı salt-okunur biçimde dener; storage, scanner ve Data Protection provider readiness durumunu değerlendirir. Migration veya veri yazma işlemi yapmaz.

Healthy/Degraded cevaplar 200, Unhealthy cevap 503 sözleşmesini kullanır. Body yalnız genel alanları içerir:

```json
{
  "status": "Healthy",
  "correlationId": "0123456789abcdef0123456789abcdef",
  "message": "Uygulama hazır."
}
```

Health body'de entry adı, exception, SQL, connection string, sunucu/database adı, kullanıcı, fiziksel path veya provider diagnostic'i yayınlanmaz. Development adapter'ları yalnız Development ortamında ready sayılır. Production'da development/no-op/unavailable adapter'ları unhealthy'dir.

## Correlation ID ve güvenli logging

- Header adı `X-Correlation-ID`'dir.
- Yalnız 1–64 karakter uzunluğunda, ilk karakteri alfanümerik ve tamamı `A-Z`, `a-z`, `0-9`, `.`, `_`, `-` kümesindeki değerler kabul edilir.
- Eksik, birden fazla, çok uzun veya geçersiz değer yerine yeni 32 karakterlik trace ID üretilir.
- Web response, Web→API request, API response, log scope ve ProblemDetails `correlationId` extension aynı değeri taşır.
- Token, TCKN, e-posta, orijinal dosya adı, connection string, SQL ve fiziksel path loglanmaz.
- SQL unavailable log'u exception nesnesini veya mesajını içermez. Beklenmeyen request hatası yalnız exception type ile kaydedilir.

## Session ve Data Protection

Web cookie ve API access token farklı güvenlik sınırlarıdır:

- Web application name: `GraduateApp.Web`
- API application name: `GraduateApp.API`

Development'ta iki host da `AddDataProtection` platform varsayılan repository keşfini kullanır. Windows'ta kullanıcı profili kullanılabiliyorsa varsayılan repository `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` altında kalıcıdır ve key material Windows DPAPI ile at-rest korunur. Bu durumda aynı uygulama ve aynı `ApplicationName`, process restart'ından sonra kendi payload'ını okuyabilir; yalnız API restart'ı normal olarak yeniden login gerektirmez. Kullanıcı profili veya kalıcı bir platform repository'si bulunmayan bir hostta framework process-içi fallback'e dönebilir ve restart sürekliliği garanti edilmez.

Development dışındaki ortamlarda, kurumsal provider seçilene kadar mevcut güvenli fallback ephemeral provider'dır. Bu fallback disk üzerinde şifrelenmemiş key-ring üretmez; restart sürekliliği sağlamaz ve Production readiness'i healthy yapmaz. Gerçek provider eklenirken bu fallback kaydı provider kaydıyla değiştirilmeli ve `IDataProtectionReadinessProbe` provider'ın güvenli, salt-okunur readiness kontrolüyle birlikte uygulanmalıdır.

Farklı `ApplicationName` değerleri, aynı repository kullanılsa bile API ve Web protector purpose zincirlerini ayırır; bir host diğerinin protected payload'ını çözemez. Aynı uygulamanın restart sürekliliği ise repository'nin gerçekten kalıcı ve erişilebilir olmasına bağlıdır.

Development restart smoke sözleşmesi şöyledir:

- Token geçerliyse kullanıcı oturumu ve gerçek API verileri kesintisiz devam eder.
- Token gerçekten geçersizse API 401 döndürür ve `ApiAccessTokenHandler` Web cookie'yi aynı request içinde yalnız bir kez kapatıp kontrollü login yönlendirmesine izin verir.
- 403, 404, 409, 429, 500 ve 503 cookie'yi kapatmaz.
- Her iki durumda da çelişkili “oturumunuz açık”/login yönlendirme döngüsü oluşmamalıdır.

Testlerde kullanılan `EphemeralDataProtectionProvider`, production runtime davranışı iddiası değil, yeniden oluşturulan process-içi test provider'ının token'ı geçersizleştirdiğini kanıtlayan bilinçli test-only senaryodur. Kalıcı test repository testleri benzersiz geçici dizin kullanır; gerçek kullanıcı key-ring'ini okumaz veya değiştirmez.

Development'ta platform varsayılan repository'nin restart boyunca çalışması kurumsal Production key store kanıtı değildir. Production'da `IDataProtectionReadinessProbe` kurumsal provider kaydıyla birlikte değiştirilmeden `/health/ready` healthy olamaz. Provider seçildiğinde aşağıdakiler ayrı ayrı kararlaştırılmalıdır:

- API ve Web için ayrı application-name/purpose izolasyonunun korunması
- Key storage konumu ve ACL/identity modeli
- At-rest key encryption ve anahtar sağlayıcısı
- Key rotation, retention ve eski key erişimi
- Multi-instance paylaşım ve disaster recovery
- Provider outage/read-only/readiness davranışı

DPAPI, sertifika, Redis, SMB veya herhangi bir cloud key store varsayılmamıştır.

## Storage ve malware scanner karar matrisi

Mevcut iş abstraction'ları korunur: `IPrivateFileStorage` ve `IFileMalwareScanner`.

| Ortam / provider | Readiness | Upload |
| --- | --- | --- |
| Development storage + development no-op scanner, Development | Healthy olabilir | Geliştirme amaçlı kabul |
| Development/no-op provider, Production | Unhealthy | 503 fail-closed |
| Unavailable provider | Unhealthy | 503 fail-closed |
| Production provider, readiness probe yok | Unhealthy | 503 fail-closed |
| Production provider + `IProductionReadinessProbe=true` | Healthy olabilir | Normal workflow |
| Scanner timeout/unavailable/ambiguous | Unhealthy veya request 503 | Belge kabul edilmez |

Production adapter'ları `IProductionReadinessProbe` uygulamalı ve probe güvenli, kısa, salt-okunur olmalıdır. Storage provider atomic create/no-overwrite, path traversal koruması, erişim kontrolü, encryption-at-rest, yedekleme ve disaster recovery sağlamalıdır. Scanner yalnız kesin temiz sonucu kabul edilebilir olarak işaretlemelidir.

Uygulama Production klasörü oluşturmaz, ACL değiştirmez ve Defender/MpCmdRun, Windows Service, SMB, Azure Blob veya S3 implementasyonu içermez.

## Altyapı seçenekleri ve karar noktaları

| Konu | Olası seçenekler | Deployment öncesi karar |
| --- | --- | --- |
| Process host | IIS, Windows Service, container/platform service | Identity, lifecycle, recycle, graceful shutdown, health probe routing |
| Private storage | Yerel NTFS, SMB, object/cloud storage | ACL/IAM, atomic write, encryption, backup, retention, HA |
| Malware scanner | Kurumsal ICAP/API/agent/service | Timeout, maksimum boyut, verdict modeli, fail-closed SLA |
| Data Protection | OS/sertifika tabanlı, distributed cache/store, managed key service | Encryption, rotation, multi-instance ve recovery |
| Secret yönetimi | Platform secret store, managed identity, deployment injection | Rotation, erişim denetimi, audit ve break-glass |

Bu seçeneklerin hiçbiri mevcut kod tarafından otomatik seçilmez.

## Deployment öncesi kontrol listesi

- Production connection string secret store'dan sağlanıyor; log/manifest içinde değil.
- SQL connection/command ve Web outer timeout sıralaması doğrulandı.
- `/health/live` bağımlılık arızasında da 200; `/health/ready` gerçek arızada 503.
- Gerçek storage/scanner implementasyonları ve readiness probe'ları bağlandı.
- Data Protection key provider ve readiness probe'u API ve Web için bağlandı.
- Web/API application-name izolasyonu korunuyor.
- Process identity ve storage/key ACL'leri uygulama dışı provisioning ile ayarlandı.
- TLS termination, forwarded headers ve trusted proxy sınırı platformda doğrulandı.
- Log redaction, retention, erişim ve correlation araması doğrulandı.
- Migration ayrı ve kontrollü release adımı olarak planlandı; uygulama başlangıcında çalışmıyor.
- Release build/test/format, EF pending-model ve vulnerable-package kapıları geçti.
- Backup/restore ve provider outage tatbikatı tamamlandı.

## Rollback kontrol listesi

- Önceki uygulama artifact'i ve uyumlu configuration sürümü hazır.
- Geri dönüşte yeni migration otomatik geri alınmıyor; ayrı DBA kararı gerekiyor.
- Eski sürümün yeni key-ring ve storage nesnelerini okuyabildiği doğrulandı.
- Rollback sonrası liveness/readiness, login, API 401 sign-out ve belge fail-closed smoke testleri çalıştırıldı.
- Secret veya key rotation geri alınacaksa erişim penceresi ve audit kaydı onaylandı.
- Correlation ID ile hata oranı ve 401/503 değişimi izlendi.

## Kurumsal altyapı belli olduğunda kalan kararlar

1. Web ve API process/topology ile multi-instance modeli.
2. SQL HA/failover yaklaşımı ve timeout değerlerinin ölçümle kalibrasyonu.
3. Private storage sağlayıcısı, retention/orphan cleanup ve backup/restore.
4. Malware scanner sağlayıcısı, verdict/SLA/timeout sözleşmesi.
5. Data Protection key store, at-rest encryption, rotation ve recovery.
6. Secret store ve workload identity.
7. Central logging/metrics/tracing, alarm eşikleri ve on-call runbook.
8. IIS/Windows Service/container, proxy ve forwarded-header güven sınırı.
9. Deployment migration, rollback ve disaster-recovery prosedürü.
