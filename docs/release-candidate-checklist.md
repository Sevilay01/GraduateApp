# GraduateApp Release Candidate kontrol listesi

Bu belge, `GraduateApp.Web` MVC/BFF, `GraduateApp.API` Web API ve
`GraduateApp.Tests` projelerinin Release Candidate kabul kapısıdır. Belge,
uygulama kodunun doğrulanması ile gerçek production altyapısının hazır olmasını
iki ayrı karar olarak ele alır.

## Kapsam ve değişmezler

- [ ] İncelenen branch ve tam commit SHA release kaydına yazıldı.
- [ ] Çalışma ağacındaki tüm değişikliklerin kaynağı ve amacı biliniyor.
- [ ] Release kapsamına yeni özellik eklenmedi; yalnız hardening, regresyon testi
  ve operasyon dokümantasyonu var.
- [ ] Browser yalnız Web/BFF ile konuşuyor; API access token'ı JavaScript'e veya
  browser storage'a açılmıyor.
- [ ] Öğrenci kimliği request alanından değil doğrulanmış `NameIdentifier`
  claim'inden alınıyor.
- [ ] Admin ve Student rol sınırları hem Web hem API tarafında uygulanıyor.
- [ ] State-changing Web endpoint'leri POST kullanıyor ve global antiforgery
  doğrulaması açık.
- [ ] API migration, seed veya schema update işlemini uygulama başlangıcında
  otomatik çalıştırmıyor.
- [ ] GraduateAppDB üzerinde migration, DDL veya DML ancak ayrı, onaylı deployment
  adımıyla çalıştırılıyor.

## Otomatik build, test ve analiz kapıları

Aşağıdaki komutlar depo kökünde ve bu sırayla çalıştırılır:

```powershell
dotnet restore GraduateApp.slnx
dotnet clean GraduateApp.slnx -c Release
dotnet build GraduateApp.slnx -c Release --no-restore
dotnet test GraduateApp.slnx -c Release --no-build
dotnet format GraduateApp.slnx --verify-no-changes --no-restore
dotnet ef migrations has-pending-model-changes --project GraduateApp.API --startup-project GraduateApp.API --configuration Release --no-build
git diff --check
dotnet list GraduateApp.slnx package --vulnerable --include-transitive
```

- [ ] Restore geçti.
- [ ] Temiz Release build geçti; warning ve error yok.
- [ ] Tüm testlerde başarısız test yok.
- [ ] LocalDB bulunmayan makinelerde SQL entegrasyon testleri başarı sayılmadı;
  atlanan testlerin tam listesi release kaydına eklendi.
- [ ] CI Windows runner'ında `MSSQLLocalDB` zorunlu ve SQL entegrasyon testleri
  atlanmadan geçti.
- [ ] Format doğrulaması geçti.
- [ ] EF model snapshot ile güncel model arasında bekleyen değişiklik yok.
- [ ] Whitespace kontrolü geçti.
- [ ] Doğrudan ve transitif NuGet paketlerinde bilinen açık yok.

## Framework-dependent Release publish

Publish çıktıları depo dışındaki benzersiz bir geçici dizine alınır:

```powershell
dotnet publish GraduateApp.API/GraduateApp.API.csproj -c Release --no-restore --no-build --self-contained false -p:DebugType=None -p:DebugSymbols=false -o <temp>\api
dotnet publish GraduateApp.Web/GraduateApp.Web.csproj -c Release --no-restore --no-build --self-contained false -p:DebugType=None -p:DebugSymbols=false -o <temp>\web
```

- [ ] API ve Web framework-dependent Release publish geçti.
- [ ] Publish dizininde `.dev-storage`, `.dev-emails`, test assembly'si, test
  verisi, `.user`, user-secrets, private key veya gerçek connection string yok.
- [ ] Release için gereksiz `.pdb`, `.xml`, coverage, `.trx`, dump veya log
  dosyası paketlenmedi.
- [ ] `appsettings.json` yalnız secret olmayan varsayılanları içeriyor; production
  değerleri deployment sırasında dışarıdan veriliyor.
- [ ] Publish dosya listesi ve hash/manifest release kaydında saklanıyor.

## Ortam yapılandırma matrisi

| Konu | Development | CI/Test | PreProduction | Production |
| --- | --- | --- | --- | --- |
| SQL | Yerel secret/user-secrets | İzole LocalDB; test DB'leri geçici | Secret store, production benzeri TLS/HA | Secret store/managed identity, TLS, HA ve backup |
| `AllowedHosts` | `localhost;127.0.0.1` | Test hostu | Yalnız gerçek host adları | Yalnız gerçek host adları |
| Web → API | Yerel HTTPS adresi | Test handler/TestServer | Gerçek iç HTTPS API adresi | Gerçek iç HTTPS API adresi |
| CORS | Normal BFF akışında boş | Boş | Gerekiyorsa kesin origin allow-list | Gerekiyorsa kesin origin allow-list |
| Data Protection | Platform varsayılan repository | Teste özel provider | Kalıcı, şifreli, paylaşımlı kurumsal provider + readiness | Kalıcı, şifreli, paylaşımlı kurumsal provider + readiness |
| Private storage | `.dev-storage`, Git dışında | Sahte/izole test provider | Gerçek private storage + salt-okunur readiness | Gerçek private storage + salt-okunur readiness |
| Malware scanner | Development no-op açıkça kabul | Kontrollü test double | Gerçek scanner, kesin temiz verdict | Gerçek scanner, kesin temiz verdict |
| E-posta | `.dev-emails`, Git dışında | Test double | Gerçek provider | Gerçek provider |
| Secret'lar | User-secrets/env | CI secret store; loglanmaz | Deployment secret store | Deployment secret store/managed identity |
| TLS/HSTS | HTTPS geliştirme profili | HTTPS TestServer sözleşmesi | Proxy/host doğrulaması + HSTS | Proxy/host doğrulaması + HSTS |
| Logging | Yerel, PII-free | Test capture | Merkezi log/metric/trace | Merkezi log/metric/trace, alarm ve retention |

Production ve PreProduction için ayrıca:

- [ ] `ConnectionStrings__DefaultConnection` güvenli kaynaktan sağlandı.
- [ ] `GraduateApi__BaseAddress`, `Web__BaseUrl` ve `AllowedHosts` gerçek
  adreslerle sınırlandı.
- [ ] `BootstrapAdmin__Email` ve `BootstrapAdmin__Password` yalnız ilk kontrollü
  bootstrap sırasında birlikte sağlandı; hesap oluşturulduktan sonra kaldırıldı.
- [ ] Timeout sırası: readiness < connection < command/iç bağımlılık < Web outer.
- [ ] Bilinen proxy ağları ve forwarded-header sınırı platformda açıkça tanımlı.

## Migration deployment planı

1. Uygulama ve veritabanı sürümleri için değişiklik penceresi/onay kaydı açılır.
2. Tam, geri yüklenebilir SQL backup alınır ve restore tatbikatının son başarılı
   kanıtı kontrol edilir.
3. Hedef veritabanında temel tablo/kolon/constraint beklentileri, duplicate
   başvurular, durum dağılımı ve `__EFMigrationsHistory` zinciri salt-okunur
   sorgularla doğrulanır.
4. Release commit'inden idempotent script depo dışındaki geçici dizine üretilir:

   ```powershell
   dotnet ef migrations script --idempotent --project GraduateApp.API --startup-project GraduateApp.API --configuration Release --no-build --output <temp>\GraduateApp.Migrations.sql
   ```

5. Script DBA ve uygulama sahibi tarafından; hedef nesne kontrolleri, uzun süren
   index/lock riski, veri dönüşümleri, `THROW` preflight'ları ve migration sırası
   açısından incelenir.
6. Uygulama trafiği ve bakım penceresi planlandığı şekilde durdurulur/dren edilir.
7. Script yalnız yetkili deployment kimliğiyle uygulanır. Uygulama process'i
   migration çalıştırmaz.
8. Migration history, şema invariants ve kritik satır sayıları salt-okunur
   doğrulanır.
9. Yeni API/Web artifact'i açılır; live/ready ve smoke testleri tamamlanır.

## Rollback ve backup/restore

- [ ] Önceki API/Web artifact'i ve uyumlu configuration hazır.
- [ ] Veritabanı `Down` migration ile otomatik geri alınmayacak; bazı `Down`
  işlemleri veri kaybı nedeniyle bilinçli olarak desteklenmiyor.
- [ ] Schema değişikliği uygulanmışsa rollback kararı DBA tarafından, doğrulanmış
  backup restore veya ileri düzeltme migration'ı arasında veriliyor.
- [ ] Restore RPO/RTO, şifreleme anahtarları, storage metadata/nesne tutarlılığı
  ve Data Protection key erişimiyle birlikte tatbik edildi.
- [ ] Rollback sonrası login, rol sınırı, profil, başvuru, belge fail-closed,
  evaluation görünürlüğü ve health smoke testleri yeniden çalıştırılıyor.
- [ ] Rollback sonrası hata oranı, 401/403/409/429/500/503 dağılımı ve correlation
  ID aramaları izleniyor.

## Health ve operasyon kabulü

- [ ] `GET /health/live` SQL/storage/scanner/Data Protection çağırmadan 200 dönüyor.
- [ ] `GET /health/ready` yalnız gerçek SQL, storage, scanner ve Data Protection
  provider'ları hazırsa 200 dönüyor.
- [ ] Her zorunlu bağımlılık arızası readiness'i 503 yapıyor; upload fail-closed.
- [ ] Health body; connection string, SQL, server/database adı, path, token,
  e-posta, TCKN, exception veya provider diagnostic'i içermiyor.
- [ ] API ve Web HTTPS yanıtlarında Production HSTS doğrulandı.
- [ ] TLS termination, redirect ve health probe routing gerçek load
  balancer/reverse proxy üzerinden doğrulandı.
- [ ] Graceful shutdown, recycle, multi-instance ve dependency outage tatbikatı
  yapıldı.

## Logging ve PII kontrolü

- [ ] Access token, reset/invitation token, parola, password hash, TCKN, e-posta,
  öğrenci adı, orijinal dosya adı, fiziksel path, connection string ve SQL
  loglanmıyor.
- [ ] Correlation ID yalnız güvenli karakter/uzunluk sözleşmesiyle kabul ediliyor.
- [ ] Kullanıcıya dönen ProblemDetails genel Türkçe mesaj içeriyor; stack trace,
  exception mesajı veya altyapı ayrıntısı yok.
- [ ] SQL unavailable kayıtları exception nesnesi/mesajı taşımıyor; beklenmeyen
  hata kaydı yalnız exception type gibi PII-free sınırlı metadata içeriyor.
- [ ] Merkezi log erişimi, redaction, retention, alarm eşikleri ve on-call
  runbook'u onaylandı.

## Otomatik kabul kapsamı

- [ ] Student/Admin login ayrımı, lockout, security-stamp ve session iptali.
- [ ] Local return URL doğrulaması ve open-redirect reddi.
- [ ] Web global antiforgery ve state-changing endpoint'lerin HTTP yöntemi.
- [ ] Student/Admin API rol sınırları ve IDOR/sahiplik kontrolleri.
- [ ] Güvenli belge türü/imzası/boyutu, malware/storage fail-closed davranışı,
  indirme başlıkları ve rowversion.
- [ ] Taslak/gönderim, zorunlu belge, duplicate, kota ve atomik transaction
  kuralları.
- [ ] GNO, sınav ve manuel puan decimal/aralık sözleşmeleri.
- [ ] Evaluation kriter toplamı 10000, ExamId bağı, snapshot, deterministik
  sıralama, finalize/publish ayrımı ve yayın öncesi öğrenci gizliliği.
- [ ] SQL outage/cancellation ProblemDetails ve cookie korunumu.
- [ ] EF çoklu koleksiyon sorgularında split-query/komut sayısı ve eksiksiz DTO.
- [ ] Migration model/snapshot ve idempotent script güvenlik testleri.

## Manuel smoke/UAT

1. Student ve Admin doğru ekranlara girer; çapraz rol URL/API denemeleri 403 veya
   güvenli yönlendirme alır.
2. Dış return URL ile login denemesi reddedilir; yerel return URL korunur.
3. Profilde GNO için `3,60`, `3.60`, `0`, `4` ve boş değer kabul edilir;
   `-0,01`, `4,01`, çoklu ayraç ve metin kontrollü Türkçe hatayla reddedilir.
4. Başka öğrenciye ait başvuru, requirement ve belge PublicID değerleriyle erişim
   denemesi varlığı açığa çıkarmayan 404 verir.
5. Geçerli PDF/JPEG/PNG yüklenir; sahte imza, yanlış tür, limit aşımı, scanner
   belirsizliği ve provider outage reddedilir.
6. Zorunlu belge eksik başvuru gönderilemez; tamamlanınca tek transaction
   sonucunda status/history/audit/snapshot tutarlı oluşur.
7. Admin stale rowversion ile status/belge/kriter işlemi yaptığında 409 alır;
   güncel değerle geçerli geçiş tamamlanır.
8. Evaluation toplam ağırlığı 10000 olmayan ilan açılmaz; submit snapshot'ı
   profil/sınav değişse de değişmez.
9. Finalize öncesi eksik uygunluk/manuel puan reddedilir; finalize sonrası öğrenci
   sonucu göremez; publish sonrası yalnız kendi sonucunu görür.
10. SQL veya API erişilemezken 503 genel Türkçe mesaj gelir; cookie yalnız gerçek
    API 401 durumunda tek kez kapatılır.
11. Production provider'lardan biri kapatıldığında readiness 503 olur ve belge
    kabul edilmez.
12. Gerçek proxy üzerinden HTTP→HTTPS, HSTS, secure cookie, live/ready ve
    correlation ID davranışı doğrulanır.

## Bilinen production blocker'ları

Aşağıdakiler uygulama kodunda sahte bir başarıyla doldurulmaz. Her biri gerçek
altyapı seçimi, uygulaması ve operasyon kanıtı gerektirir:

- [ ] Gerçek private storage provider'ı, ACL/IAM, encryption-at-rest, retention,
  orphan cleanup, backup ve disaster recovery.
- [ ] Gerçek malware scanner, verdict/timeout/SLA ve salt-okunur readiness probe.
- [ ] API ve Web için kalıcı, şifreli, multi-instance Data Protection key store,
  rotation, retention ve recovery.
- [ ] Connection string/bootstrap/e-posta/provider secret'ları için kurumsal
  secret store veya managed identity.
- [ ] SQL Server HA/failover, kapasite, timeout kalibrasyonu, backup ve düzenli
  restore tatbikatı.
- [ ] Gerçek e-posta provider'ı ve token içermeyen operasyonel gözlem.
- [ ] Merkezi PII-redacted logging, metrics, tracing, dashboard, alert ve on-call.
- [ ] IIS/Windows Service/container, load balancer, TLS, trusted proxy,
  forwarded-header ve deployment topology kararı.
- [ ] DBA onaylı migration deployment ve database rollback/restore runbook'u.

Bu maddeler tamamlanmadıkça `/health/ready` Production'da bilinçli olarak healthy
olmayabilir ve production trafik açılışı için **No-Go** kararı verilir.

## Go / No-Go kararı

İki karar ayrı kaydedilir:

| Karar | Go koşulu | Mevcut karar |
| --- | --- | --- |
| Kod kalitesi / RC artifact | Tüm otomatik kapılar geçer, başarısız test yok, LocalDB testleri geçer, publish temiz ve açık P0/P1 kod bulgusu yok | **Go** — 27 Temmuz 2026 yerel RC kapıları geçti |
| Production trafik açılışı | Kod kalitesi Go ve tüm production blocker'ları/manuel smoke/backup-restore/operasyon onayları tamam | **No-Go** — altyapı ve manuel kabul kanıtları bekleniyor |

## 27 Temmuz 2026 yerel RC denetim kaydı

- Branch: `chore/release-candidate-hardening`
- Başlangıç HEAD: `87a2a748ce384552fe965aeb6b5314798f24f5e3`
- Başlangıç çalışma ağacı: temiz; staged/untracked dosya yok.
- Restore, clean, build, test, format, pending-model, whitespace ve doğrudan/
  transitif vulnerable-package kapıları geçti.
- Release build: 0 warning, 0 error.
- Test sonucu: **616 passed, 0 failed, 0 skipped**.
- LocalDB ile doğrulanamayan test: **yok**. Tüm SQL entegrasyon testleri benzersiz
  geçici test veritabanlarında çalıştı; `GraduateAppDB` kullanılmadı.
- API publish: 49 dosya, 20.816.558 byte.
- Web publish: 132 dosya, 6.268.765 byte.
- Publish taraması: PDB, source map, test assembly/fixture, `.dev-storage`,
  `.dev-emails`, user-secret, private key ve gerçek credential eşleşmesi yok.
- Publish manifesti: 181 kayıt; SHA-256
  `4B745F2C731D5290D493EC1676A8FE3EE0A182EC570A5BD7DAFD1BDBC6DAB7B3`.
- İdempotent migration scripti yalnız depo dışına üretildi; 11 migration içeriyor.
  SHA-256:
  `615995074DC74180B9CA8C4DCAF0CDD5C71A83BECC0A6E380941B64DF3C77E2F`.
- Script hiçbir veritabanına uygulanmadı; `GraduateAppDB` üzerinde migration, DDL
  veya DML çalıştırılmadı.
- Manual browser/proxy/UAT ve gerçek production provider/backup-restore tatbikatı
  bu yerel otomatik denetimin kapsamı dışındadır; production kararı bu nedenle
  No-Go'dur.

Onay kaydı:

- Release commit:
- Artifact/manifest:
- Migration script hash:
- Kod kalitesi kararı ve onaylayan:
- Production kararı ve onaylayan:
- Açık risk/istisna kayıtları:
