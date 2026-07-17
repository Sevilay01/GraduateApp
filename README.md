# GraduateApp

GraduateApp, öğrencilerin açık yüksek lisans programlarını görüntüleyip başvuru yapabildiği; başvurularını takip edebildiği ve yöneticilerin başvuruları filtreleyip güvenli durum geçişleriyle değerlendirebildiği ASP.NET Core tabanlı bir başvuru sistemidir.

## Mimari

- `GraduateApp.Web`: MVC kullanıcı arayüzü ve BFF katmanı. Tarayıcı API belirtecini doğrudan kullanmaz; belirteç şifreli, `HttpOnly` oturum çerezinde tutulur ve typed `HttpClient` tarafından server-side API isteklerine eklenir.
- `GraduateApp.API`: Kimlik doğrulama, yetkilendirme, profil, program ve başvuru iş kuralları. Entity Framework Core üzerinden SQL Server kullanır.
- `GraduateApp.Tests`: Servis kuralları, rol korumaları ve Web/API hata sözleşmeleri için xUnit testleri.

Parolalar ASP.NET Core `PasswordHasher<TUser>` ile hashlenir. API erişim belirteçleri Data Protection ile şifreli ve süreli olarak oluşturulur. Öğrenci kimliği formdan veya URL'den değil, doğrulanmış `NameIdentifier` claim'inden alınır.

## Gereksinimler

- .NET SDK 10.0 veya üzeri
- SQL Server
- Geliştirme HTTPS sertifikası (`dotnet dev-certs https --trust`)
- EF Core CLI (`dotnet ef`) — SDK/Visual Studio ile yoksa ayrıca kurulmalıdır

## Güvenli yerel kurulum

Depoya connection string, parola veya token yazmayın. API secret değerlerini user-secrets ile sağlayın:

```powershell
dotnet user-secrets init --project GraduateApp.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<yerel-sql-server-connection-string>" --project GraduateApp.API
dotnet user-secrets set "BootstrapAdmin:Email" "<admin-email>" --project GraduateApp.API
dotnet user-secrets set "BootstrapAdmin:Password" "<güçlü-geçici-parola>" --project GraduateApp.API
```

Environment variable karşılıkları:

```text
ConnectionStrings__DefaultConnection
BootstrapAdmin__Email
BootstrapAdmin__Password
Web__BaseUrl
Cors__AllowedOrigins__0
GraduateApi__BaseAddress
```

`BootstrapAdmin` değerleri birlikte sağlanır. E-posta mevcut değilse API başlangıcında admin oluşturulur; parola hashlenir ve ilk girişte değiştirilmesi zorunludur. Uygulamada gerçek bir varsayılan admin parolası yoktur. Bootstrap secret'larını hesap oluşturulduktan sonra ortamdan kaldırın.

Web, API'yi varsayılan geliştirme adresi olan `https://localhost:7037/` üzerinden çağırır. Farklı bir adres için `GraduateApi__BaseAddress` kullanın. API'nin parola sıfırlama bağlantısında kullandığı Web adresi `Web__BaseUrl` ile yapılandırılır.

## Veritabanı ve migration

Bu depo database-first bir şemadan geldiği ve geçmiş EF migration kaydı içermediği için `HardenExistingSchema` migration'ı mevcut temel tabloları yeniden oluşturmaz. Migration:

- kimlik doğrulama/lockout/security-stamp alanlarını ekler;
- parola hash kolonlarını veri kaybetmeden genişletir;
- program açık/son tarih alanlarını ekler;
- öğrenci-program çifti için unique index ekler;
- durum allow-list constraint'i, durum geçmişi, rowversion ve güvenli audit tablosu ekler;
- mevcut Türkçe legacy durum değerlerini anlamını koruyarak canonical değerlere dönüştürür.

Önce veritabanı yedeği alın ve script'i inceleyin:

```powershell
dotnet ef migrations script --idempotent --project GraduateApp.API --startup-project GraduateApp.API --output migration.sql
dotnet ef database update --project GraduateApp.API --startup-project GraduateApp.API
```

Migration aşağıdaki durumlarda veri silmek yerine hata verip transaction'ı durdurur:

- aynı öğrenci/program için yinelenen başvuru;
- tanımsız başvuru durumu;
- 500 karakteri aşan geçmiş notu;
- normalize edildiğinde yinelenen e-posta.

Bu kayıtlar DBA/ürün sahibi kararıyla çözülmeden migration'ı zorlamayın. Migration `Down` metodu da veri kaybı riski nedeniyle otomatik tablo/kolon silmez.

Migration sonrasında mevcut programlar güvenli varsayılan olarak kapalıdır. Başvuru açılacak programlar için `IsOpen = 1` ve gerekiyorsa UTC `ApplicationDeadlineUtc` değeri yetkili bir veritabanı operasyonuyla belirlenmelidir.

## Uygulamaları çalıştırma

İki terminal kullanın:

```powershell
dotnet run --project GraduateApp.API --launch-profile https
dotnet run --project GraduateApp.Web --launch-profile https
```

Güvenli çerez nedeniyle Web'in HTTPS profilini kullanın. CORS varsayılan olarak kapalıdır; Web API'yi server-side çağırdığı için normal yerel kullanımda CORS gerekmez. Doğrudan güvenilir bir browser origin gerekiyorsa yalnızca ilgili origin'i `Cors__AllowedOrigins__0` ile ekleyin.

## Parola sıfırlama

Parola sıfırlama yanıtı, hesabın varlığını açıklamayan genel bir mesajdır. Token:

- kriptografik rastgele üretilir;
- veritabanında yalnızca SHA-256 hash olarak tutulur;
- 30 dakika geçerlidir;
- tek kullanımlıktır;
- kullanımdan sonra security stamp'i yenileyerek mevcut API oturumunu geçersiz kılar.

Development ortamında reset e-postası, Git tarafından yok sayılan `GraduateApp.API/.dev-emails/` dizinine yazılır; token loglanmaz. Production ortamında gerçek e-posta sağlayıcısına bağlı bir `IPasswordResetEmailSender` implementasyonu yapılandırılmadan parola sıfırlama bildirimi gönderilmez ve yalnızca genel bir operasyon hatası loglanır.

## Build ve test

```powershell
dotnet restore GraduateApp.slnx
dotnet build GraduateApp.slnx --configuration Release --no-restore -p:UseAppHost=false
dotnet test GraduateApp.Tests/GraduateApp.Tests.csproj --configuration Release --no-build
dotnet format GraduateApp.slnx --verify-no-changes --no-restore
```

Test paketi şu kritik davranışları kapsar:

- geçerli/geçersiz öğrenci ve admin girişi;
- Web ve API admin/öğrenci rol korumaları;
- öğrencinin yalnızca kendi başvurularını görmesi;
- duplicate ve kapalı program başvurusunun reddi;
- geçersiz durum geçişinin reddi;
- reset token süre aşımı ve tekrar kullanımının reddi;
- API hata/bozuk gövde durumunda Web'in kontrollü mesaj üretmesi.

## Production notları

- Data Protection key ring'i container/çoklu instance ortamında kalıcı ve erişimi sınırlı ortak depoda tutun.
- Gerçek e-posta sağlayıcısı ekleyin; reset linkini veya token'ı production loglarına yazmayın.
- Connection string, bootstrap secret ve origin listesini deployment secret store üzerinden sağlayın.
- Proxy arkasında HTTPS yönlendirme ve forwarded headers politikasını yalnızca bilinen proxy ağlarıyla yapılandırın.
- Legacy düz metin veya uyumsuz parola kayıtları otomatik kabul edilmez; kullanıcı güvenli parola sıfırlama akışını kullanmalıdır.
- Temiz bir veritabanı kurulumu için kurumun mevcut temel database-first şema script'i gerekir; bu depodaki migration var olan şemayı sertleştirmek içindir.
