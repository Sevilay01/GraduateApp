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

`BootstrapAdmin` değerleri birlikte sağlanır ve yalnızca sistemde hiç yönetici yokken ilk yönetici hesabını oluşturur. E-posta doğrulanır, parola hashlenir ve ilk girişte değiştirilmesi zorunludur. Sistemde bir yönetici varken farklı bootstrap e-postasıyla ikinci yönetici oluşturulmaz; ek yönetici desteği ileride yetkili bir yönetim akışıyla sağlanmalıdır. Uygulamada gerçek bir varsayılan admin parolası yoktur. Bootstrap secret'larını hesap oluşturulduktan sonra ortamdan kaldırın; secret'ların kaldırılması mevcut hesabı silmez veya değiştirmez.

Web, API'yi varsayılan geliştirme adresi olan `https://localhost:7037/` üzerinden çağırır. Farklı bir adres için `GraduateApi__BaseAddress` kullanın. API'nin parola sıfırlama bağlantısında kullandığı Web adresi `Web__BaseUrl` ile yapılandırılır.

### Üniversite kataloğunu güvenli kurma

Öğrenci profilindeki üniversite seçimi mevcut `Universities` kataloğunu kullanır. Kataloğu doğrudan SQL ile doldurmayın ve development başlangıcına otomatik seed eklemeyin. Admin rolüyle Web uygulamasında **Üniversiteler** sayfasını açıp üniversite adlarını form üzerinden ekleyin. API, adı trim eder ve doğrular; veritabanı collation kurallarıyla yinelenen adları güvenli 409 yanıtıyla reddeder. Başarılı ekleme ile PII içermeyen `UniversityCreated` audit kaydı aynı transaction içinde yazılır.

Bu akış yalnızca listeleme ve eklemeyi destekler. Mevcut `EducationInfo` yabancı anahtar geçmişini korumak için üniversite silme veya yeniden adlandırma işlemi sunulmaz. Katalog boşsa ya da yüklenemezse öğrenci profilinde eğitim alanları devre dışı kalır; profilin diğer alanları güncellenmeye devam edebilir.

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

### Güvenli başvuru belgeleri migration'ı

`SecureApplicationDocuments` ileri migration'ı yeni belge tablolarını, başvuru `PublicID` alanını, `UsesDocumentWorkflow` grandfather bayrağını, rowversion alanlarını, filtered/unique indexleri ve Draft durumunu ekler. Mevcut başvuruların durumu değiştirilmez ve tamamı `UsesDocumentWorkflow = 0` olarak kalır. Başvuru `PublicID` değerleri nullable kolon aşamasıyla eklenir, `NEWID()` ile doldurulur, null/duplicate kontrolünden sonra `NOT NULL` ve unique yapılır.

Migration; beklenen temel tablo/kolon veya durum constraint'i bulunmazsa, hedef belge nesneleri migration geçmişi dışında önceden varsa, tanımsız durum ya da aynı öğrenci/ilan için duplicate başvuru bulunursa Türkçe `THROW` ile transaction'ı durdurur. Çalıştırmadan önce tam yedek alın, idempotent script'i inceleyin ve en az tablo/kolon/constraint adlarını, duplicate başvuruları, durum dağılımını ve `__EFMigrationsHistory` zincirini doğrulayın. Bu migration'ın `Down` işlemi belge geçmişini yok edeceği için desteklenmez; geri dönüş uygulama binary'si ve restore edilmiş veritabanı yedeğiyle planlanmalıdır.

`EducationInfo.DiplomaPath`, `EducationInfo.TranscriptPath` ve `ReferenceLetter.FilePath` alanları legacy kabul edilir. Sahiplikleri ve dosya içerikleri doğrulanamadığı için silinmez, migration ile taşınmaz ve yeni güvenli belge kayıtlarıyla otomatik birleştirilmez.

### Başvuru değerlendirme ve sonuç yayımlama

Yeni oluşturulan dönemsel ilanlar değerlendirme iş akışını kullanır; migration öncesi ilan ve başvurular `UsesEvaluationWorkflow = 0` ile legacy davranışını korur. Bir değerlendirme ilanı açılmadan önce en az bir kriter tanımlanmalı, pozitif kriter ağırlıklarının toplamı tam olarak `10000` basis point olmalı ve kriter kodları ile eşitlik bozma öncelikleri ilan içinde benzersiz olmalıdır. Desteklenen kaynaklar:

- `UndergraduateGpa`: maksimum ham puan tam olarak 4 olan tek lisans GNO kriteri;
- `ExamScore`: ilanın zorunlu sınav koşullarından birine bağlı, açıkça tanımlanmış pozitif maksimum puanlı kriter;
- `ManualScore`: maksimum ham puan tam olarak 100 olan yönetici puanı.

İlk taslak başvuru oluştuğunda ilanın kriter politikası kilitlenir. Gönderim anında GNO ve sınav ham puanları ile kriter kodu, adı, kaynak türü, maksimum puan, ağırlık ve eşitlik önceliği başvuruya snapshot olarak kopyalanır; manuel bileşen boş kalır. Puanlama yalnızca `decimal` aritmetik kullanır:

```text
NormalizedExact = (RawScore / MaximumRawScore) * 100
NormalizedScore = round_away_from_zero(NormalizedExact, 4)
WeightedScore   = round_away_from_zero(NormalizedExact * WeightBasisPoints / 10000, 4)
TotalScore      = round_away_from_zero(sum(WeightedScore), 4)
```

Ağırlıklı puan, dört haneye yuvarlanmış `NormalizedScore` üzerinden değil yuvarlanmamış `NormalizedExact` üzerinden hesaplanır; böylece ara yuvarlama puanı değiştirmez. Kalıcı ve gösterilen değerler dört ondalık haneye `MidpointRounding.AwayFromZero` ile yuvarlanır.

Yeni iş akışında kontenjan gönderim sırasında başvuruyu engellemez; kontenjan sıralama sonunda kabul sayısını belirler. Legacy ilanların mevcut gönderim-kota kuralı değişmez. Yönetici önce başvuruyu `UnderReview` durumuna alır, zorunlu güncel belgeleri onaylar, uygunluk kararı verir ve manuel bileşenleri tamamlar. Uygun adaylar toplam puan azalan, kriter öncelik sırasındaki normalize puanlar azalan, başvuru tarihi artan ve son olarak başvuru `PublicID` artan biçimde deterministik sıralanır.

`Finalize` ilan kapalıyken, son başvuru tarihi geçtikten ve tüm adaylar tamamlandıktan sonra sıra ve sonuçları tek transaction içinde dondurur; öğrenciye hiçbir puan, sıra veya karar açmaz ve başvuru durumlarını değiştirmez. Ayrı onay ekranındaki `Publish` işlemi sonuçları tek transaction içinde yayımlar, kabul edilenleri `Approved`, diğer sonuçları `Rejected` yapar, durum geçmişi ve PII içermeyen audit kayıtlarını ekler. Öğrenci yalnızca kendi başvurusunun yayımlanmış sonucunu ayrı sahiplik kontrollü endpoint üzerinden görebilir. Tüm yönetici mutasyonları admin rolü, antiforgery, rowversion ve güvenli Türkçe hata sözleşmeleriyle korunur.

`AddApplicationEvaluationAndResults` ileri migration'ı kriter, değerlendirme ve bileşen tablolarını; unique/filtered indexleri; check constraint'leri ve rowversion alanlarını ekler. Migration beklenen güvenli temel şema yoksa, hedef nesneler migration geçmişi dışında mevcutsa veya tanımsız başvuru durumu bulunursa veri değiştirmeden Türkçe `THROW` ile durur. Mevcut ilan ve başvuruları otomatik olarak yeni akışa almaz. `Down` veri kaybı riski nedeniyle desteklenmez; geri dönüş yeni uygulama binary'si yerine doğrulanmış veritabanı yedeği ve önceki uygulama sürümüyle planlanmalıdır.

Değerlendirme smoke testi:

1. Kapalı yeni ilan oluşturun; toplamı 10000 olmayan politika ile açılamadığını, geçerli kriterler ve zorunlu belge koşuluyla açılabildiğini doğrulayın.
2. Taslak oluşturulduktan sonra kriter ekleme, değiştirme ve silmenin reddedildiğini doğrulayın.
3. Öğrenci GNO/sınav bilgilerini ve zorunlu belgelerini tamamlayıp başvursun; otomatik değerlerin submit anında snapshot olduğunu ve kota dolu olsa da yeni akış başvurusunun gönderilebildiğini doğrulayın.
4. Admin başvuruyu incelemeye alsın, belgeleri onaylasın, uygunluk ve manuel puanı kaydetsin; stale rowversion denemelerinin 409 verdiğini doğrulayın.
5. Önizleme sırasını kontrol edin, ilanı kapatıp son tarihten sonra kesinleştirin; öğrenci ekranında sonuç bulunmadığını doğrulayın.
6. Ayrı onay sayfasından yayımlayın; statü/geçmiş/audit kayıtlarını ve yalnızca başvuru sahibinin sonuç kırılımını görebildiğini doğrulayın.

## Güvenli belge depolama

Development ortamında belgeler API projesinin `GraduateApp.API/.dev-storage/` dizininde, `wwwroot` dışında ve Git tarafından izlenmeden tutulur. Kullanıcının dosya adı depolama anahtarı yapılmaz; tahmin edilemez opaque anahtar kullanılır ve metadata dışında fiziksel yol veya erişilebilir URL veritabanına yazılmaz. Geçici dosyalar doğrulama sonunda temizlenir; depolama yazımı geçici dosya + atomik taşıma kullanır ve mevcut anahtarın üzerine yazmaz.

Başlangıçta yalnızca PDF, JPEG ve PNG kabul edilir. Uzantı ile bildirilen Content-Type yeterli sayılmaz; PDF/JPEG/PNG imzası doğrulanır ve üç değer birbiriyle eşleşmelidir. Varsayılan global sınır 10 MB'dir ve `DocumentUpload__MaximumBytes` ile en fazla 100 MB olacak şekilde yapılandırılabilir. İlan belge koşulu sınırı global sınırı aşamaz. Boş dosya, executable içeriğe sahip sahte PDF, desteklenmeyen arşiv/ofis/SVG/HTML türleri ve aynı requirement'ın güncel sürümüyle birebir aynı SHA-256 içeriği reddedilir. Hash streaming sırasında hesaplanır; hash, ObjectKey ve fiziksel yol DTO'lara dönmez.

Development scanner'ı açıkça development-only no-op implementasyondur ve "virüs tarandı" garantisi vermez. Production ortamında gerçek `IPrivateFileStorage` ve `IFileMalwareScanner` implementasyonları DI üzerinden bağlanmadan yükleme fail closed reddedilir. Production sağlayıcısı atomic create/no-overwrite, path traversal koruması, erişim kontrolü, encryption-at-rest, yedekleme ve felaket kurtarma beklentilerini karşılamalıdır. Dosya retention, hukuki saklama süresi ve orphan/expired draft temizliği için henüz ürün ve operasyon politikası gerekir; fiziksel geçmiş otomatik silinmez.

### Belge iş akışı smoke testi

1. Admin olarak açık bir dönemsel ilana zorunlu PDF ve isteğe bağlı görsel belge koşulu ekleyin; aynı kodun ikinci kez ve global sınır üstünde boyutun reddedildiğini doğrulayın.
2. Öğrenci olarak taslak oluşturun; requirement adlarının snapshot olarak göründüğünü ve Draft'ın admin listesinde/kontenjan sayımında olmadığını doğrulayın.
3. Sahte `.pdf`, yanlış Content-Type, boş ve limit üstü dosyaların reddedildiğini; geçerli PDF/JPEG/PNG'nin yüklendiğini kontrol edin.
4. Zorunlu belge eksikken submit'in eksik adlarıyla reddedildiğini, isteğe bağlı belge eksikken submit'in başarılı olduğunu doğrulayın.
5. Başka bir öğrenci oturumuyla başvuru/belge PublicID'sini deneyin ve kaynak varlığını açıklamayan 404 alın.
6. Admin olarak güncel belgeyi güvenli indirin; `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff` ve `Cache-Control: no-store` header'larını doğrulayın.
7. Belgeyi gerekçeyle reddedin, öğrenci yeniden yüklesin; eski sürümün geçmişte kaldığını, yeni sürümün `Pending` ve tek current kayıt olduğunu doğrulayın.
8. Stale RowVersion ile requirement/review güncellemesinin Türkçe 409 verdiğini ve tüm zorunlu belgeler onaylanmadan başvurunun Approved yapılamadığını doğrulayın.

Merkezi `LoginIdentities` migration'ı daha önce uygulanmış olabileceğinden geçmiş migration dosyası değiştirilmez. İleri doğrulama migration'ı; kaynak e-postaların null/boş olmamasını, canonical normalize değerleri, roller arası çakışmaları ve her hesabın tam olarak bir doğru merkezi kimliğe bağlı olmasını veri değiştirmeden denetler.

### Birden fazla yönetici kaydı

Mevcut veritabanında birden fazla yönetici kaydı bulunabilir; uygulama ve migration'lar bu hesapları otomatik silmez. Kullanılmayan bir yöneticiyi kaldırmadan önce en az şu bağımlılıklar incelenmelidir:

- `ApplicationStatusHistory.ChangedByAdminID`
- `PasswordResetTokens.AdminID`
- `SystemLogs.AdminID`
- `SecurityAuditLogs.ActorAdminID`
- `LoginIdentities.AdminID`

Bağımlı kayıt varsa geçmiş ve audit bütünlüğünü bozacak fiziksel silme yerine, ayrı bir ileri geliştirmede kalıcı yönetici aktiflik durumu ve oturum iptaliyle devre dışı bırakma tasarımı tercih edilmelidir. Bootstrap mevcut yöneticileri silmez, güncellemez veya ikinci yönetici üretmek için kullanılmaz.

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

## Sürekli entegrasyon

GitHub Actions kalite kapısı, tüm pull request'lerde ve `main` dalına yapılan push'larda Windows üzerinde .NET 10 ile çalışır. Workflow restore, Release build, tüm testler, biçim doğrulaması, idempotent EF migration script üretimi, doğrudan ve transitif NuGet güvenlik açığı taraması ile whitespace kontrolünü uygular.

SQL entegrasyon testleri yalnızca CI runner'ındaki izole LocalDB veritabanlarını kullanır; LocalDB kullanılamıyorsa testler sessizce atlanmaz ve job açık bir hatayla durur. Migration adımı yalnızca runner'ın geçici klasöründe SQL üretir; herhangi bir veritabanına migration uygulamaz. CI bootstrap admin veya production secret oluşturmaz.

## Production notları

- Data Protection key ring'i container/çoklu instance ortamında kalıcı ve erişimi sınırlı ortak depoda tutun.
- Gerçek e-posta sağlayıcısı ekleyin; reset linkini veya token'ı production loglarına yazmayın.
- Connection string, bootstrap secret ve origin listesini deployment secret store üzerinden sağlayın.
- Proxy arkasında HTTPS yönlendirme ve forwarded headers politikasını yalnızca bilinen proxy ağlarıyla yapılandırın.
- Legacy düz metin veya uyumsuz parola kayıtları otomatik kabul edilmez; kullanıcı güvenli parola sıfırlama akışını kullanmalıdır.
- Temiz bir veritabanı kurulumu için kurumun mevcut temel database-first şema script'i gerekir; bu depodaki migration var olan şemayı sertleştirmek içindir.
