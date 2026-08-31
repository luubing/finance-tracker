; ModuleID = 'marshal_methods.arm64-v8a.ll'
source_filename = "marshal_methods.arm64-v8a.ll"
target datalayout = "e-m:e-i8:8:32-i16:16:32-i64:64-i128:128-n32:64-S128"
target triple = "aarch64-unknown-linux-android21"

%struct.MarshalMethodName = type {
	i64, ; uint64_t id
	ptr ; char* name
}

%struct.MarshalMethodsManagedClass = type {
	i32, ; uint32_t token
	ptr ; MonoClass klass
}

@assembly_image_cache = dso_local local_unnamed_addr global [368 x ptr] zeroinitializer, align 8

; Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array
@assembly_image_cache_hashes = dso_local local_unnamed_addr constant [1104 x i64] [
	i64 u0x001e58127c546039, ; 0: lib_System.Globalization.dll.so => 42
	i64 u0x0024d0f62dee05bd, ; 1: Xamarin.KotlinX.Coroutines.Core.dll => 325
	i64 u0x0071cf2d27b7d61e, ; 2: lib_Xamarin.AndroidX.SwipeRefreshLayout.dll.so => 304
	i64 u0x01109b0e4d99e61f, ; 3: System.ComponentModel.Annotations.dll => 13
	i64 u0x02123411c4e01926, ; 4: lib_Xamarin.AndroidX.Navigation.Runtime.dll.so => 292
	i64 u0x022f31be406de945, ; 5: Microsoft.Extensions.Options.ConfigurationExtensions => 220
	i64 u0x0284512fad379f7e, ; 6: System.Runtime.Handles => 107
	i64 u0x02a4c5a44384f885, ; 7: Microsoft.Extensions.Caching.Memory => 199
	i64 u0x02abedc11addc1ed, ; 8: lib_Mono.Android.Runtime.dll.so => 174
	i64 u0x02f55bf70672f5c8, ; 9: lib_System.IO.FileSystem.DriveInfo.dll.so => 48
	i64 u0x032267b2a94db371, ; 10: lib_Xamarin.AndroidX.AppCompat.dll.so => 246
	i64 u0x03621c804933a890, ; 11: System.Buffers => 7
	i64 u0x0363ac97a4cb84e6, ; 12: SQLitePCLRaw.provider.e_sqlite3.dll => 235
	i64 u0x0399610510a38a38, ; 13: lib_System.Private.DataContractSerialization.dll.so => 88
	i64 u0x043032f1d071fae0, ; 14: ru/Microsoft.Maui.Controls.resources => 353
	i64 u0x044440a55165631e, ; 15: lib-cs-Microsoft.Maui.Controls.resources.dll.so => 331
	i64 u0x046eb1581a80c6b0, ; 16: vi/Microsoft.Maui.Controls.resources => 359
	i64 u0x047408741db2431a, ; 17: Xamarin.AndroidX.DynamicAnimation => 266
	i64 u0x0517ef04e06e9f76, ; 18: System.Net.Primitives => 72
	i64 u0x0565d18c6da3de38, ; 19: Xamarin.AndroidX.RecyclerView => 296
	i64 u0x057bf9fa9fb09f7c, ; 20: Microsoft.Data.Sqlite.dll => 193
	i64 u0x0581db89237110e9, ; 21: lib_System.Collections.dll.so => 12
	i64 u0x05989cb940b225a9, ; 22: Microsoft.Maui.dll => 226
	i64 u0x05a1c25e78e22d87, ; 23: lib_System.Runtime.CompilerServices.Unsafe.dll.so => 104
	i64 u0x05ef98b6a1db882c, ; 24: lib_Microsoft.Data.Sqlite.dll.so => 193
	i64 u0x06076b5d2b581f08, ; 25: zh-HK/Microsoft.Maui.Controls.resources => 360
	i64 u0x06388ffe9f6c161a, ; 26: System.Xml.Linq.dll => 159
	i64 u0x06600c4c124cb358, ; 27: System.Configuration.dll => 19
	i64 u0x067f95c5ddab55b3, ; 28: lib_Xamarin.AndroidX.Fragment.Ktx.dll.so => 271
	i64 u0x0680a433c781bb3d, ; 29: Xamarin.AndroidX.Collection.Jvm => 253
	i64 u0x0690533f9fc14683, ; 30: lib_Microsoft.AspNetCore.Components.dll.so => 186
	i64 u0x069fff96ec92a91d, ; 31: System.Xml.XPath.dll => 164
	i64 u0x070b0847e18dab68, ; 32: Xamarin.AndroidX.Emoji2.ViewsHelper.dll => 268
	i64 u0x0739448d84d3b016, ; 33: lib_Xamarin.AndroidX.VectorDrawable.dll.so => 308
	i64 u0x07469f2eecce9e85, ; 34: mscorlib.dll => 170
	i64 u0x07588bca46bae0b4, ; 35: lib_FluentValidation.DependencyInjectionExtensions.dll.so => 181
	i64 u0x07c57877c7ba78ad, ; 36: ru/Microsoft.Maui.Controls.resources.dll => 353
	i64 u0x07dcdc7460a0c5e4, ; 37: System.Collections.NonGeneric => 10
	i64 u0x08122e52765333c8, ; 38: lib_Microsoft.Extensions.Logging.Debug.dll.so => 218
	i64 u0x088610fc2509f69e, ; 39: lib_Xamarin.AndroidX.VectorDrawable.Animated.dll.so => 309
	i64 u0x08a7c865576bbde7, ; 40: System.Reflection.Primitives => 98
	i64 u0x08c9d051a4a817e5, ; 41: Xamarin.AndroidX.CustomView.PoolingContainer.dll => 264
	i64 u0x08f3c9788ee2153c, ; 42: Xamarin.AndroidX.DrawerLayout => 265
	i64 u0x09138715c92dba90, ; 43: lib_System.ComponentModel.Annotations.dll.so => 13
	i64 u0x0919c28b89381a0b, ; 44: lib_Microsoft.Extensions.Options.dll.so => 219
	i64 u0x092266563089ae3e, ; 45: lib_System.Collections.NonGeneric.dll.so => 10
	i64 u0x09d144a7e214d457, ; 46: System.Security.Cryptography => 129
	i64 u0x09e2b9f743db21a8, ; 47: lib_System.Reflection.Metadata.dll.so => 97
	i64 u0x0a2a764cf63a28df, ; 48: DeepCloner.Core => 179
	i64 u0x0a805f95d98f597b, ; 49: lib_Microsoft.Extensions.Caching.Abstractions.dll.so => 198
	i64 u0x0abb3e2b271edc45, ; 50: System.Threading.Channels.dll => 143
	i64 u0x0b06b1feab070143, ; 51: System.Formats.Tar => 39
	i64 u0x0b3b632c3bbee20c, ; 52: sk/Microsoft.Maui.Controls.resources => 354
	i64 u0x0b6aff547b84fbe9, ; 53: Xamarin.KotlinX.Serialization.Core.Jvm => 328
	i64 u0x0be2e1f8ce4064ed, ; 54: Xamarin.AndroidX.ViewPager => 311
	i64 u0x0c3ca6cc978e2aae, ; 55: pt-BR/Microsoft.Maui.Controls.resources => 350
	i64 u0x0c59ad9fbbd43abe, ; 56: Mono.Android => 175
	i64 u0x0c65741e86371ee3, ; 57: lib_Xamarin.Android.Glide.GifDecoder.dll.so => 240
	i64 u0x0c74af560004e816, ; 58: Microsoft.Win32.Registry.dll => 5
	i64 u0x0c7790f60165fc06, ; 59: lib_Microsoft.Maui.Essentials.dll.so => 227
	i64 u0x0c83c82812e96127, ; 60: lib_System.Net.Mail.dll.so => 68
	i64 u0x0cce4bce83380b7f, ; 61: Xamarin.AndroidX.Security.SecurityCrypto => 301
	i64 u0x0d13cd7cce4284e4, ; 62: System.Security.SecureString => 132
	i64 u0x0d63f4f73521c24f, ; 63: lib_Xamarin.AndroidX.SavedState.SavedState.Ktx.dll.so => 300
	i64 u0x0e04e702012f8463, ; 64: Xamarin.AndroidX.Emoji2 => 267
	i64 u0x0e14e73a54dda68e, ; 65: lib_System.Net.NameResolution.dll.so => 69
	i64 u0x0e442fe1d1afe06a, ; 66: lib_FinanceTracker.App.dll.so => 0
	i64 u0x0f37dd7a62ae99af, ; 67: lib_Xamarin.AndroidX.Collection.Ktx.dll.so => 254
	i64 u0x0f5e7abaa7cf470a, ; 68: System.Net.HttpListener => 67
	i64 u0x1001f97bbe242e64, ; 69: System.IO.UnmanagedMemoryStream => 57
	i64 u0x102a31b45304b1da, ; 70: Xamarin.AndroidX.CustomView => 263
	i64 u0x1065c4cb554c3d75, ; 71: System.IO.IsolatedStorage.dll => 52
	i64 u0x10f6cfcbcf801616, ; 72: System.IO.Compression.Brotli => 43
	i64 u0x114443cdcf2091f1, ; 73: System.Security.Cryptography.Primitives => 127
	i64 u0x11a603952763e1d4, ; 74: System.Net.Mail => 68
	i64 u0x11a70d0e1009fb11, ; 75: System.Net.WebSockets.dll => 83
	i64 u0x11f26371eee0d3c1, ; 76: lib_Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll.so => 281
	i64 u0x12128b3f59302d47, ; 77: lib_System.Xml.Serialization.dll.so => 161
	i64 u0x123639456fb056da, ; 78: System.Reflection.Emit.Lightweight.dll => 94
	i64 u0x12521e9764603eaa, ; 79: lib_System.Resources.Reader.dll.so => 101
	i64 u0x125b7f94acb989db, ; 80: Xamarin.AndroidX.RecyclerView.dll => 296
	i64 u0x12d3b63863d4ab0b, ; 81: lib_System.Threading.Overlapped.dll.so => 144
	i64 u0x134eab1061c395ee, ; 82: System.Transactions => 154
	i64 u0x138567fa954faa55, ; 83: Xamarin.AndroidX.Browser => 250
	i64 u0x13a01de0cbc3f06c, ; 84: lib-fr-Microsoft.Maui.Controls.resources.dll.so => 337
	i64 u0x13beedefb0e28a45, ; 85: lib_System.Xml.XmlDocument.dll.so => 165
	i64 u0x13f1e5e209e91af4, ; 86: lib_Java.Interop.dll.so => 172
	i64 u0x13f1e880c25d96d1, ; 87: he/Microsoft.Maui.Controls.resources => 338
	i64 u0x143d8ea60a6a4011, ; 88: Microsoft.Extensions.DependencyInjection.Abstractions => 206
	i64 u0x1497051b917530bd, ; 89: lib_System.Net.WebSockets.dll.so => 83
	i64 u0x14d612a531c79c05, ; 90: Xamarin.JSpecify.dll => 322
	i64 u0x14e68447938213b7, ; 91: Xamarin.AndroidX.Collection.Ktx.dll => 254
	i64 u0x152a448bd1e745a7, ; 92: Microsoft.Win32.Primitives => 4
	i64 u0x1557de0138c445f4, ; 93: lib_Microsoft.Win32.Registry.dll.so => 5
	i64 u0x15bdc156ed462f2f, ; 94: lib_System.IO.FileSystem.dll.so => 51
	i64 u0x15e300c2c1668655, ; 95: System.Resources.Writer.dll => 103
	i64 u0x16054fdcb6b3098b, ; 96: Microsoft.Extensions.DependencyModel.dll => 207
	i64 u0x16bf2a22df043a09, ; 97: System.IO.Pipes.dll => 56
	i64 u0x16ea2b318ad2d830, ; 98: System.Security.Cryptography.Algorithms => 122
	i64 u0x16eeae54c7ebcc08, ; 99: System.Reflection.dll => 100
	i64 u0x17125c9a85b4929f, ; 100: lib_netstandard.dll.so => 171
	i64 u0x1716866f7416792e, ; 101: lib_System.Security.AccessControl.dll.so => 120
	i64 u0x174f71c46216e44a, ; 102: Xamarin.KotlinX.Coroutines.Core => 325
	i64 u0x1752c12f1e1fc00c, ; 103: System.Core => 21
	i64 u0x1791d47293d97a1b, ; 104: lib_Npgsql.EntityFrameworkCore.PostgreSQL.dll.so => 230
	i64 u0x17b56e25558a5d36, ; 105: lib-hu-Microsoft.Maui.Controls.resources.dll.so => 341
	i64 u0x17f9358913beb16a, ; 106: System.Text.Encodings.Web => 139
	i64 u0x1809fb23f29ba44a, ; 107: lib_System.Reflection.TypeExtensions.dll.so => 99
	i64 u0x18402a709e357f3b, ; 108: lib_Xamarin.KotlinX.Serialization.Core.Jvm.dll.so => 328
	i64 u0x18a9befae51bb361, ; 109: System.Net.WebClient => 79
	i64 u0x18f0ce884e87d89a, ; 110: nb/Microsoft.Maui.Controls.resources.dll => 347
	i64 u0x19777fba3c41b398, ; 111: Xamarin.AndroidX.Startup.StartupRuntime.dll => 303
	i64 u0x19a4c090f14ebb66, ; 112: System.Security.Claims => 121
	i64 u0x1a91866a319e9259, ; 113: lib_System.Collections.Concurrent.dll.so => 8
	i64 u0x1aac34d1917ba5d3, ; 114: lib_System.dll.so => 168
	i64 u0x1aad60783ffa3e5b, ; 115: lib-th-Microsoft.Maui.Controls.resources.dll.so => 356
	i64 u0x1aea8f1c3b282172, ; 116: lib_System.Net.Ping.dll.so => 71
	i64 u0x1b4b7a1d0d265fa2, ; 117: Xamarin.Android.Glide.DiskLruCache => 239
	i64 u0x1b8700ce6e547c0b, ; 118: lib_Microsoft.AspNetCore.Components.Forms.dll.so => 188
	i64 u0x1bbdb16cfa73e785, ; 119: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android => 282
	i64 u0x1bc766e07b2b4241, ; 120: Xamarin.AndroidX.ResourceInspection.Annotation.dll => 297
	i64 u0x1c292b1598348d77, ; 121: Microsoft.Extensions.Diagnostics.dll => 208
	i64 u0x1c5217a9e4973753, ; 122: lib_Microsoft.Extensions.FileProviders.Physical.dll.so => 213
	i64 u0x1c753b5ff15bce1b, ; 123: Mono.Android.Runtime.dll => 174
	i64 u0x1cd47467799d8250, ; 124: System.Threading.Tasks.dll => 148
	i64 u0x1d23eafdc6dc346c, ; 125: System.Globalization.Calendars.dll => 40
	i64 u0x1da4110562816681, ; 126: Xamarin.AndroidX.Security.SecurityCrypto.dll => 301
	i64 u0x1db6820994506bf5, ; 127: System.IO.FileSystem.AccessControl.dll => 47
	i64 u0x1dbb0c2c6a999acb, ; 128: System.Diagnostics.StackTrace => 30
	i64 u0x1e3d87657e9659bc, ; 129: Xamarin.AndroidX.Navigation.UI => 294
	i64 u0x1e71143913d56c10, ; 130: lib-ko-Microsoft.Maui.Controls.resources.dll.so => 345
	i64 u0x1e7c31185e2fb266, ; 131: lib_System.Threading.Tasks.Parallel.dll.so => 147
	i64 u0x1ed8fcce5e9b50a0, ; 132: Microsoft.Extensions.Options.dll => 219
	i64 u0x1f055d15d807e1b2, ; 133: System.Xml.XmlSerializer => 166
	i64 u0x1f1ed22c1085f044, ; 134: lib_System.Diagnostics.FileVersionInfo.dll.so => 28
	i64 u0x1f61df9c5b94d2c1, ; 135: lib_System.Numerics.dll.so => 86
	i64 u0x1f750bb5421397de, ; 136: lib_Xamarin.AndroidX.Tracing.Tracing.dll.so => 305
	i64 u0x20237ea48006d7a8, ; 137: lib_System.Net.WebClient.dll.so => 79
	i64 u0x209375905fcc1bad, ; 138: lib_System.IO.Compression.Brotli.dll.so => 43
	i64 u0x20fab3cf2dfbc8df, ; 139: lib_System.Diagnostics.Process.dll.so => 29
	i64 u0x210c5bb16b7260a2, ; 140: lib_Microsoft.Extensions.Validation.dll.so => 222
	i64 u0x2110167c128cba15, ; 141: System.Globalization => 42
	i64 u0x21419508838f7547, ; 142: System.Runtime.CompilerServices.VisualC => 105
	i64 u0x2174319c0d835bc9, ; 143: System.Runtime => 119
	i64 u0x2198e5bc8b7153fa, ; 144: Xamarin.AndroidX.Annotation.Experimental.dll => 244
	i64 u0x219ea1b751a4dee4, ; 145: lib_System.IO.Compression.ZipFile.dll.so => 45
	i64 u0x21cc7e445dcd5469, ; 146: System.Reflection.Emit.ILGeneration => 93
	i64 u0x220fd4f2e7c48170, ; 147: th/Microsoft.Maui.Controls.resources => 356
	i64 u0x224538d85ed15a82, ; 148: System.IO.Pipes => 56
	i64 u0x22908438c6bed1af, ; 149: lib_System.Threading.Timer.dll.so => 151
	i64 u0x237be844f1f812c7, ; 150: System.Threading.Thread.dll => 149
	i64 u0x23807c59646ec4f3, ; 151: lib_Microsoft.EntityFrameworkCore.dll.so => 194
	i64 u0x23852b3bdc9f7096, ; 152: System.Resources.ResourceManager => 102
	i64 u0x23986dd7e5d4fc01, ; 153: System.IO.FileSystem.Primitives.dll => 49
	i64 u0x2407aef2bbe8fadf, ; 154: System.Console => 20
	i64 u0x240abe014b27e7d3, ; 155: Xamarin.AndroidX.Core.dll => 259
	i64 u0x247619fe4413f8bf, ; 156: System.Runtime.Serialization.Primitives.dll => 116
	i64 u0x24de8d301281575e, ; 157: Xamarin.Android.Glide => 237
	i64 u0x252073cc3caa62c2, ; 158: fr/Microsoft.Maui.Controls.resources.dll => 337
	i64 u0x256b8d41255f01b1, ; 159: Xamarin.Google.Crypto.Tink.Android => 318
	i64 u0x25a0a7eff76ea08e, ; 160: SQLitePCLRaw.batteries_v2.dll => 232
	i64 u0x2662c629b96b0b30, ; 161: lib_Xamarin.Kotlin.StdLib.dll.so => 323
	i64 u0x268c1439f13bcc29, ; 162: lib_Microsoft.Extensions.Primitives.dll.so => 221
	i64 u0x26a670e154a9c54b, ; 163: System.Reflection.Extensions.dll => 96
	i64 u0x26d077d9678fe34f, ; 164: System.IO.dll => 58
	i64 u0x273f3515de5faf0d, ; 165: id/Microsoft.Maui.Controls.resources.dll => 342
	i64 u0x2742545f9094896d, ; 166: hr/Microsoft.Maui.Controls.resources => 340
	i64 u0x27458c1163a5ce22, ; 167: Microsoft.Extensions.Validation => 222
	i64 u0x274d85d83ad40513, ; 168: lib_Xamarin.AndroidX.Window.WindowCore.dll.so => 314
	i64 u0x2759af78ab94d39b, ; 169: System.Net.WebSockets => 83
	i64 u0x27b2b16f3e9de038, ; 170: Xamarin.Google.Crypto.Tink.Android.dll => 318
	i64 u0x27b410442fad6cf1, ; 171: Java.Interop.dll => 172
	i64 u0x27b97e0d52c3034a, ; 172: System.Diagnostics.Debug => 26
	i64 u0x2801845a2c71fbfb, ; 173: System.Net.Primitives.dll => 72
	i64 u0x28559fbeb7cea4c0, ; 174: Util.Reflection.dll => 236
	i64 u0x286835e259162700, ; 175: lib_Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll.so => 295
	i64 u0x28e52865585a1ebe, ; 176: Microsoft.Extensions.Diagnostics.Abstractions.dll => 209
	i64 u0x2949f3617a02c6b2, ; 177: Xamarin.AndroidX.ExifInterface => 269
	i64 u0x29aeab763a527e52, ; 178: lib_Xamarin.AndroidX.Navigation.Common.Android.dll.so => 290
	i64 u0x2a128783efe70ba0, ; 179: uk/Microsoft.Maui.Controls.resources.dll => 358
	i64 u0x2a3b095612184159, ; 180: lib_System.Net.NetworkInformation.dll.so => 70
	i64 u0x2a6507a5ffabdf28, ; 181: System.Diagnostics.TraceSource.dll => 33
	i64 u0x2ad156c8e1354139, ; 182: fi/Microsoft.Maui.Controls.resources => 336
	i64 u0x2ad5d6b13b7a3e04, ; 183: System.ComponentModel.DataAnnotations.dll => 14
	i64 u0x2af298f63581d886, ; 184: System.Text.RegularExpressions.dll => 141
	i64 u0x2afc1c4f898552ee, ; 185: lib_System.Formats.Asn1.dll.so => 38
	i64 u0x2b148910ed40fbf9, ; 186: zh-Hant/Microsoft.Maui.Controls.resources.dll => 362
	i64 u0x2b4d4904cebfa4e9, ; 187: Microsoft.Extensions.FileSystemGlobbing => 214
	i64 u0x2b6989d78cba9a15, ; 188: Xamarin.AndroidX.Concurrent.Futures.dll => 255
	i64 u0x2c8bd14bb93a7d82, ; 189: lib-pl-Microsoft.Maui.Controls.resources.dll.so => 349
	i64 u0x2cbd9262ca785540, ; 190: lib_System.Text.Encoding.CodePages.dll.so => 136
	i64 u0x2cc9e1fed6257257, ; 191: lib_System.Reflection.Emit.Lightweight.dll.so => 94
	i64 u0x2cd723e9fe623c7c, ; 192: lib_System.Private.Xml.Linq.dll.so => 90
	i64 u0x2d169d318a968379, ; 193: System.Threading.dll => 152
	i64 u0x2d20145f27cfc1d2, ; 194: Xamarin.AndroidX.Window.WindowCore.Jvm.dll => 315
	i64 u0x2d47774b7d993f59, ; 195: sv/Microsoft.Maui.Controls.resources.dll => 355
	i64 u0x2d5ffcae1ad0aaca, ; 196: System.Data.dll => 24
	i64 u0x2db915caf23548d2, ; 197: System.Text.Json.dll => 140
	i64 u0x2dcaa0bb15a4117a, ; 198: System.IO.UnmanagedMemoryStream.dll => 57
	i64 u0x2e2ced2c3c6a1edc, ; 199: lib_System.Threading.AccessControl.dll.so => 142
	i64 u0x2e5a40c319acb800, ; 200: System.IO.FileSystem => 51
	i64 u0x2e6f1f226821322a, ; 201: el/Microsoft.Maui.Controls.resources.dll => 334
	i64 u0x2e8ff3fae87a8245, ; 202: lib_Microsoft.JSInterop.dll.so => 223
	i64 u0x2f02f94df3200fe5, ; 203: System.Diagnostics.Process => 29
	i64 u0x2f2e98e1c89b1aff, ; 204: System.Xml.ReaderWriter => 160
	i64 u0x2f5911d9ba814e4e, ; 205: System.Diagnostics.Tracing => 34
	i64 u0x2f84070a459bc31f, ; 206: lib_System.Xml.dll.so => 167
	i64 u0x2feb4d2fcda05cfd, ; 207: Microsoft.Extensions.Caching.Abstractions.dll => 198
	i64 u0x3033937e1dfee52b, ; 208: FluentValidation => 180
	i64 u0x309ee9eeec09a71e, ; 209: lib_Xamarin.AndroidX.Fragment.dll.so => 270
	i64 u0x30c6dda129408828, ; 210: System.IO.IsolatedStorage => 52
	i64 u0x310d9651ec86c411, ; 211: Microsoft.Extensions.FileProviders.Embedded => 212
	i64 u0x31195fef5d8fb552, ; 212: _Microsoft.Android.Resource.Designer.dll => 367
	i64 u0x312c8ed623cbfc8d, ; 213: Xamarin.AndroidX.Window.dll => 313
	i64 u0x31496b779ed0663d, ; 214: lib_System.Reflection.DispatchProxy.dll.so => 92
	i64 u0x32243413e774362a, ; 215: Xamarin.AndroidX.CardView.dll => 251
	i64 u0x3235427f8d12dae1, ; 216: lib_System.Drawing.Primitives.dll.so => 35
	i64 u0x329753a17a517811, ; 217: fr/Microsoft.Maui.Controls.resources => 337
	i64 u0x32aa989ff07a84ff, ; 218: lib_System.Xml.ReaderWriter.dll.so => 160
	i64 u0x33642d5508314e46, ; 219: Microsoft.Extensions.FileSystemGlobbing.dll => 214
	i64 u0x33829542f112d59b, ; 220: System.Collections.Immutable => 9
	i64 u0x33a31443733849fe, ; 221: lib-es-Microsoft.Maui.Controls.resources.dll.so => 335
	i64 u0x341abc357fbb4ebf, ; 222: lib_System.Net.Sockets.dll.so => 78
	i64 u0x346a212343615ac5, ; 223: lib_System.Linq.AsyncEnumerable.dll.so => 59
	i64 u0x3496c1e2dcaf5ecc, ; 224: lib_System.IO.Pipes.AccessControl.dll.so => 55
	i64 u0x34bd01fd4be06ee3, ; 225: lib_Microsoft.Extensions.FileProviders.Composite.dll.so => 211
	i64 u0x34dfd74fe2afcf37, ; 226: Microsoft.Maui => 226
	i64 u0x34e292762d9615df, ; 227: cs/Microsoft.Maui.Controls.resources.dll => 331
	i64 u0x3508234247f48404, ; 228: Microsoft.Maui.Controls => 224
	i64 u0x353590da528c9d22, ; 229: System.ComponentModel.Annotations => 13
	i64 u0x3549870798b4cd30, ; 230: lib_Xamarin.AndroidX.ViewPager2.dll.so => 312
	i64 u0x355282fc1c909694, ; 231: Microsoft.Extensions.Configuration => 200
	i64 u0x3552fc5d578f0fbf, ; 232: Xamarin.AndroidX.Arch.Core.Common => 248
	i64 u0x355c649948d55d97, ; 233: lib_System.Runtime.Intrinsics.dll.so => 111
	i64 u0x35ea9d1c6834bc8c, ; 234: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll => 285
	i64 u0x36263608556d5d42, ; 235: Npgsql.dll => 229
	i64 u0x3628ab68db23a01a, ; 236: lib_System.Diagnostics.Tools.dll.so => 32
	i64 u0x3673b042508f5b6b, ; 237: lib_System.Runtime.Extensions.dll.so => 106
	i64 u0x36740f1a8ecdc6c4, ; 238: System.Numerics => 86
	i64 u0x36b2b50fdf589ae2, ; 239: System.Reflection.Emit.Lightweight => 94
	i64 u0x36cada77dc79928b, ; 240: System.IO.MemoryMappedFiles => 53
	i64 u0x374ef46b06791af6, ; 241: System.Reflection.Primitives.dll => 98
	i64 u0x376bf93e521a5417, ; 242: lib_Xamarin.Jetbrains.Annotations.dll.so => 321
	i64 u0x37bc29f3183003b6, ; 243: lib_System.IO.dll.so => 58
	i64 u0x380134e03b1e160a, ; 244: System.Collections.Immutable.dll => 9
	i64 u0x38049b5c59b39324, ; 245: System.Runtime.CompilerServices.Unsafe => 104
	i64 u0x385c17636bb6fe6e, ; 246: Xamarin.AndroidX.CustomView.dll => 263
	i64 u0x38869c811d74050e, ; 247: System.Net.NameResolution.dll => 69
	i64 u0x393c226616977fdb, ; 248: lib_Xamarin.AndroidX.ViewPager.dll.so => 311
	i64 u0x395e37c3334cf82a, ; 249: lib-ca-Microsoft.Maui.Controls.resources.dll.so => 330
	i64 u0x39c3107c28752af1, ; 250: lib_Microsoft.Extensions.FileProviders.Abstractions.dll.so => 210
	i64 u0x3ab5859054645f72, ; 251: System.Security.Cryptography.Primitives.dll => 127
	i64 u0x3ad75090c3fac0e9, ; 252: lib_Xamarin.AndroidX.ResourceInspection.Annotation.dll.so => 297
	i64 u0x3ae44ac43a1fbdbb, ; 253: System.Runtime.Serialization => 118
	i64 u0x3b860f9932505633, ; 254: lib_System.Text.Encoding.Extensions.dll.so => 137
	i64 u0x3be6248c2bc7dc8c, ; 255: Microsoft.JSInterop.dll => 223
	i64 u0x3be99b43dd39dd37, ; 256: Xamarin.AndroidX.SavedState.SavedState.Android => 299
	i64 u0x3c3aafb6b3a00bf6, ; 257: lib_System.Security.Cryptography.X509Certificates.dll.so => 128
	i64 u0x3c4049146b59aa90, ; 258: System.Runtime.InteropServices.JavaScript => 108
	i64 u0x3c7c495f58ac5ee9, ; 259: Xamarin.Kotlin.StdLib => 323
	i64 u0x3c7e5ed3d5db71bb, ; 260: System.Security => 133
	i64 u0x3cd9d281d402eb9b, ; 261: Xamarin.AndroidX.Browser.dll => 250
	i64 u0x3d1c50cc001a991e, ; 262: Xamarin.Google.Guava.ListenableFuture.dll => 320
	i64 u0x3d2b1913edfc08d7, ; 263: lib_System.Threading.ThreadPool.dll.so => 150
	i64 u0x3d46f0b995082740, ; 264: System.Xml.Linq => 159
	i64 u0x3d8a8f400514a790, ; 265: Xamarin.AndroidX.Fragment.Ktx.dll => 271
	i64 u0x3d9c2a242b040a50, ; 266: lib_Xamarin.AndroidX.Core.dll.so => 259
	i64 u0x3da7781d6333a8fe, ; 267: SQLitePCLRaw.batteries_v2 => 232
	i64 u0x3db495de2204755c, ; 268: Microsoft.Extensions.Configuration.FileExtensions => 203
	i64 u0x3dbb6b9f5ab90fa7, ; 269: lib_Xamarin.AndroidX.DynamicAnimation.dll.so => 266
	i64 u0x3e5441657549b213, ; 270: Xamarin.AndroidX.ResourceInspection.Annotation => 297
	i64 u0x3e57d4d195c53c2e, ; 271: System.Reflection.TypeExtensions => 99
	i64 u0x3e616ab4ed1f3f15, ; 272: lib_System.Data.dll.so => 24
	i64 u0x3e7f8912b96e5065, ; 273: Microsoft.AspNetCore.Components.WebView.dll => 190
	i64 u0x3f1d226e6e06db7e, ; 274: Xamarin.AndroidX.SlidingPaneLayout.dll => 302
	i64 u0x3f510adf788828dd, ; 275: System.Threading.Tasks.Extensions => 146
	i64 u0x3f6f5914291cdcf7, ; 276: Microsoft.Extensions.Hosting.Abstractions => 215
	i64 u0x407a10bb4bf95829, ; 277: lib_Xamarin.AndroidX.Navigation.Common.dll.so => 289
	i64 u0x40c98b6bd77346d4, ; 278: Microsoft.VisualBasic.dll => 3
	i64 u0x41833cf766d27d96, ; 279: mscorlib => 170
	i64 u0x41cab042be111c34, ; 280: lib_Xamarin.AndroidX.AppCompat.AppCompatResources.dll.so => 247
	i64 u0x423a9ecc4d905a88, ; 281: lib_System.Resources.ResourceManager.dll.so => 102
	i64 u0x423bf51ae7def810, ; 282: System.Xml.XPath => 164
	i64 u0x42462ff15ddba223, ; 283: System.Resources.Reader.dll => 101
	i64 u0x4291015ff4e5ef71, ; 284: Xamarin.AndroidX.Core.ViewTree.dll => 261
	i64 u0x42a31b86e6ccc3f0, ; 285: System.Diagnostics.Contracts => 25
	i64 u0x430e95b891249788, ; 286: lib_System.Reflection.Emit.dll.so => 95
	i64 u0x43375950ec7c1b6a, ; 287: netstandard.dll => 171
	i64 u0x434c4e1d9284cdae, ; 288: Mono.Android.dll => 175
	i64 u0x43505013578652a0, ; 289: lib_Xamarin.AndroidX.Activity.Ktx.dll.so => 242
	i64 u0x437d06c381ed575a, ; 290: lib_Microsoft.VisualBasic.dll.so => 3
	i64 u0x43950f84de7cc79a, ; 291: pl/Microsoft.Maui.Controls.resources.dll => 349
	i64 u0x43c077442b230f64, ; 292: Xamarin.AndroidX.Tracing.Tracing.Android => 306
	i64 u0x43e8ca5bc927ff37, ; 293: lib_Xamarin.AndroidX.Emoji2.ViewsHelper.dll.so => 268
	i64 u0x44390bf51172cd08, ; 294: lib_Microsoft.AspNetCore.Components.Authorization.dll.so => 187
	i64 u0x448bd33429269b19, ; 295: Microsoft.CSharp => 1
	i64 u0x44934faa0726b40f, ; 296: BemIt => 178
	i64 u0x4499fa3c8e494654, ; 297: lib_System.Runtime.Serialization.Primitives.dll.so => 116
	i64 u0x4515080865a951a5, ; 298: Xamarin.Kotlin.StdLib.dll => 323
	i64 u0x453c1277f85cf368, ; 299: lib_Microsoft.EntityFrameworkCore.Abstractions.dll.so => 195
	i64 u0x4545802489b736b9, ; 300: Xamarin.AndroidX.Fragment.Ktx => 271
	i64 u0x454b4d1e66bb783c, ; 301: Xamarin.AndroidX.Lifecycle.Process => 278
	i64 u0x45c40276a42e283e, ; 302: System.Diagnostics.TraceSource => 33
	i64 u0x45d443f2a29adc37, ; 303: System.AppContext.dll => 6
	i64 u0x45fcc9fd66f25095, ; 304: Microsoft.Extensions.DependencyModel => 207
	i64 u0x46a4213bc97fe5ae, ; 305: lib-ru-Microsoft.Maui.Controls.resources.dll.so => 353
	i64 u0x47358bd471172e1d, ; 306: lib_System.Xml.Linq.dll.so => 159
	i64 u0x47691e452d0c4eef, ; 307: lib_Util.Reflection.dll.so => 236
	i64 u0x47daf4e1afbada10, ; 308: pt/Microsoft.Maui.Controls.resources => 351
	i64 u0x480c0a47dd42dd81, ; 309: lib_System.IO.MemoryMappedFiles.dll.so => 53
	i64 u0x49e952f19a4e2022, ; 310: System.ObjectModel => 87
	i64 u0x49f9e6948a8131e4, ; 311: lib_Xamarin.AndroidX.VersionedParcelable.dll.so => 310
	i64 u0x4a5667b2462a664b, ; 312: lib_Xamarin.AndroidX.Navigation.UI.dll.so => 294
	i64 u0x4a7a18981dbd56bc, ; 313: System.IO.Compression.FileSystem.dll => 44
	i64 u0x4aa5c60350917c06, ; 314: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll.so => 277
	i64 u0x4b07a0ed0ab33ff4, ; 315: System.Runtime.Extensions.dll => 106
	i64 u0x4b576d47ac054f3c, ; 316: System.IO.FileSystem.AccessControl => 47
	i64 u0x4b7b6532ded934b7, ; 317: System.Text.Json => 140
	i64 u0x4c2029a97af23a8d, ; 318: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android => 287
	i64 u0x4c7755cf07ad2d5f, ; 319: System.Net.Http.Json.dll => 65
	i64 u0x4ca014ceac582c86, ; 320: Microsoft.EntityFrameworkCore.Relational.dll => 196
	i64 u0x4cc5f15266470798, ; 321: lib_Xamarin.AndroidX.Loader.dll.so => 288
	i64 u0x4cf6f67dc77aacd2, ; 322: System.Net.NetworkInformation.dll => 70
	i64 u0x4d3183dd245425d4, ; 323: System.Net.WebSockets.Client.dll => 82
	i64 u0x4d479f968a05e504, ; 324: System.Linq.Expressions.dll => 60
	i64 u0x4d4db7205172d1ec, ; 325: DeepCloner.Core.dll => 179
	i64 u0x4d55a010ffc4faff, ; 326: System.Private.Xml => 91
	i64 u0x4d5cbe77561c5b2e, ; 327: System.Web.dll => 157
	i64 u0x4d77512dbd86ee4c, ; 328: lib_Xamarin.AndroidX.Arch.Core.Common.dll.so => 248
	i64 u0x4d7793536e79c309, ; 329: System.ServiceProcess => 135
	i64 u0x4d95fccc1f67c7ca, ; 330: System.Runtime.Loader.dll => 112
	i64 u0x4db014bf0ff1c9c1, ; 331: System.Linq.AsyncEnumerable => 59
	i64 u0x4dcf44c3c9b076a2, ; 332: it/Microsoft.Maui.Controls.resources.dll => 343
	i64 u0x4dd9247f1d2c3235, ; 333: Xamarin.AndroidX.Loader.dll => 288
	i64 u0x4df510084e2a0bae, ; 334: Microsoft.JSInterop => 223
	i64 u0x4e2aeee78e2c4a87, ; 335: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller => 295
	i64 u0x4e32f00cb0937401, ; 336: Mono.Android.Runtime => 174
	i64 u0x4e5eea4668ac2b18, ; 337: System.Text.Encoding.CodePages => 136
	i64 u0x4ebd0c4b82c5eefc, ; 338: lib_System.Threading.Channels.dll.so => 143
	i64 u0x4ee8eaa9c9c1151a, ; 339: System.Globalization.Calendars => 40
	i64 u0x4f21ee6ef9eb527e, ; 340: ca/Microsoft.Maui.Controls.resources => 330
	i64 u0x4fd5f3ee53d0a4f0, ; 341: SQLitePCLRaw.lib.e_sqlite3.android => 234
	i64 u0x4fdc964ec1888e25, ; 342: lib_Microsoft.Extensions.Configuration.Binder.dll.so => 202
	i64 u0x5037f0be3c28c7a3, ; 343: lib_Microsoft.Maui.Controls.dll.so => 224
	i64 u0x50c3a29b21050d45, ; 344: System.Linq.Parallel.dll => 61
	i64 u0x5116b21580ae6eb0, ; 345: Microsoft.Extensions.Configuration.Binder.dll => 202
	i64 u0x5131bbe80989093f, ; 346: Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll => 284
	i64 u0x516324a5050a7e3c, ; 347: System.Net.WebProxy => 81
	i64 u0x516d6f0b21a303de, ; 348: lib_System.Diagnostics.Contracts.dll.so => 25
	i64 u0x51bb8a2afe774e32, ; 349: System.Drawing => 36
	i64 u0x5247c5c32a4140f0, ; 350: System.Resources.Reader => 101
	i64 u0x526bb15e3c386364, ; 351: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll => 281
	i64 u0x526ce79eb8e90527, ; 352: lib_System.Net.Primitives.dll.so => 72
	i64 u0x52829f00b4467c38, ; 353: lib_System.Data.Common.dll.so => 22
	i64 u0x529ffe06f39ab8db, ; 354: Xamarin.AndroidX.Core => 259
	i64 u0x52ff996554dbf352, ; 355: Microsoft.Maui.Graphics => 228
	i64 u0x535f7e40e8fef8af, ; 356: lib-sk-Microsoft.Maui.Controls.resources.dll.so => 354
	i64 u0x53978aac584c666e, ; 357: lib_System.Security.Cryptography.Cng.dll.so => 123
	i64 u0x53a96d5c86c9e194, ; 358: System.Net.NetworkInformation => 70
	i64 u0x53be1038a61e8d44, ; 359: System.Runtime.InteropServices.RuntimeInformation.dll => 109
	i64 u0x53c3014b9437e684, ; 360: lib-zh-HK-Microsoft.Maui.Controls.resources.dll.so => 360
	i64 u0x5435e6f049e9bc37, ; 361: System.Security.Claims.dll => 121
	i64 u0x54795225dd1587af, ; 362: lib_System.Runtime.dll.so => 119
	i64 u0x547a34f14e5f6210, ; 363: Xamarin.AndroidX.Lifecycle.Common.dll => 273
	i64 u0x54b851bc9b470503, ; 364: Xamarin.AndroidX.Navigation.Common.Android => 290
	i64 u0x556e8b63b660ab8b, ; 365: Xamarin.AndroidX.Lifecycle.Common.Jvm.dll => 274
	i64 u0x5588627c9a108ec9, ; 366: System.Collections.Specialized => 11
	i64 u0x55a898e4f42e3fae, ; 367: Microsoft.VisualBasic.Core.dll => 2
	i64 u0x55fa0c610fe93bb1, ; 368: lib_System.Security.Cryptography.OpenSsl.dll.so => 126
	i64 u0x56442b99bc64bb47, ; 369: System.Runtime.Serialization.Xml.dll => 117
	i64 u0x56a8b26e1aeae27b, ; 370: System.Threading.Tasks.Dataflow => 145
	i64 u0x56f932d61e93c07f, ; 371: System.Globalization.Extensions => 41
	i64 u0x571c5cfbec5ae8e2, ; 372: System.Private.Uri => 89
	i64 u0x576499c9f52fea31, ; 373: Xamarin.AndroidX.Annotation => 243
	i64 u0x578cd35c91d7b347, ; 374: lib_SQLitePCLRaw.core.dll.so => 233
	i64 u0x579a06fed6eec900, ; 375: System.Private.CoreLib.dll => 177
	i64 u0x57adda3c951abb33, ; 376: Microsoft.Extensions.Hosting.Abstractions.dll => 215
	i64 u0x57c542c14049b66d, ; 377: System.Diagnostics.DiagnosticSource => 27
	i64 u0x581a8bd5cfda563e, ; 378: System.Threading.Timer => 151
	i64 u0x584ac38e21d2fde1, ; 379: Microsoft.Extensions.Configuration.Binder => 202
	i64 u0x58601b2dda4a27b9, ; 380: lib-ja-Microsoft.Maui.Controls.resources.dll.so => 344
	i64 u0x58688d9af496b168, ; 381: Microsoft.Extensions.DependencyInjection.dll => 205
	i64 u0x588c167a79db6bfb, ; 382: lib_Xamarin.Google.ErrorProne.Annotations.dll.so => 319
	i64 u0x5906028ae5151104, ; 383: Xamarin.AndroidX.Activity.Ktx => 242
	i64 u0x595a356d23e8da9a, ; 384: lib_Microsoft.CSharp.dll.so => 1
	i64 u0x59f9e60b9475085f, ; 385: lib_Xamarin.AndroidX.Annotation.Experimental.dll.so => 244
	i64 u0x5a745f5101a75527, ; 386: lib_System.IO.Compression.FileSystem.dll.so => 44
	i64 u0x5a89a886ae30258d, ; 387: lib_Xamarin.AndroidX.CoordinatorLayout.dll.so => 258
	i64 u0x5a8f6699f4a1caa9, ; 388: lib_System.Threading.dll.so => 152
	i64 u0x5ae9cd33b15841bf, ; 389: System.ComponentModel => 18
	i64 u0x5b54391bdc6fcfe6, ; 390: System.Private.DataContractSerialization => 88
	i64 u0x5b5f0e240a06a2a2, ; 391: da/Microsoft.Maui.Controls.resources.dll => 332
	i64 u0x5b8109e8e14c5e3e, ; 392: System.Globalization.Extensions.dll => 41
	i64 u0x5bddd04d72a9e350, ; 393: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx => 277
	i64 u0x5bdf16b09da116ab, ; 394: Xamarin.AndroidX.Collection => 252
	i64 u0x5c019d5266093159, ; 395: lib_Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android.dll.so => 282
	i64 u0x5c30a4a35f9cc8c4, ; 396: lib_System.Reflection.Extensions.dll.so => 96
	i64 u0x5c393624b8176517, ; 397: lib_Microsoft.Extensions.Logging.dll.so => 216
	i64 u0x5c53c29f5073b0c9, ; 398: System.Diagnostics.FileVersionInfo => 28
	i64 u0x5c87463c575c7616, ; 399: lib_System.Globalization.Extensions.dll.so => 41
	i64 u0x5d0a4a29b02d9d3c, ; 400: System.Net.WebHeaderCollection.dll => 80
	i64 u0x5d25ef991dd9a85c, ; 401: Microsoft.AspNetCore.Components.WebView.Maui.dll => 191
	i64 u0x5d40c9b15181641f, ; 402: lib_Xamarin.AndroidX.Emoji2.dll.so => 267
	i64 u0x5d6ca10d35e9485b, ; 403: lib_Xamarin.AndroidX.Concurrent.Futures.dll.so => 255
	i64 u0x5d6d49cf7d0b234c, ; 404: FinanceTracker.Core.dll => 363
	i64 u0x5d7ec76c1c703055, ; 405: System.Threading.Tasks.Parallel => 147
	i64 u0x5db0cbbd1028510e, ; 406: lib_System.Runtime.InteropServices.dll.so => 110
	i64 u0x5db30905d3e5013b, ; 407: Xamarin.AndroidX.Collection.Jvm.dll => 253
	i64 u0x5e467bc8f09ad026, ; 408: System.Collections.Specialized.dll => 11
	i64 u0x5e5173b3208d97e7, ; 409: System.Runtime.Handles.dll => 107
	i64 u0x5ea92fdb19ec8c4c, ; 410: System.Text.Encodings.Web.dll => 139
	i64 u0x5eb8046dd40e9ac3, ; 411: System.ComponentModel.Primitives => 16
	i64 u0x5ec272d219c9aba4, ; 412: System.Security.Cryptography.Csp.dll => 124
	i64 u0x5eee1376d94c7f5e, ; 413: System.Net.HttpListener.dll => 67
	i64 u0x5f0537da2862ac79, ; 414: BemIt.dll => 178
	i64 u0x5f36ccf5c6a57e24, ; 415: System.Xml.ReaderWriter.dll => 160
	i64 u0x5f4294b9b63cb842, ; 416: System.Data.Common => 22
	i64 u0x5f7399e166075632, ; 417: lib_SQLitePCLRaw.lib.e_sqlite3.android.dll.so => 234
	i64 u0x5f9a2d823f664957, ; 418: lib-el-Microsoft.Maui.Controls.resources.dll.so => 334
	i64 u0x5fa6da9c3cd8142a, ; 419: lib_Xamarin.KotlinX.Serialization.Core.dll.so => 327
	i64 u0x5fac98e0b37a5b9d, ; 420: System.Runtime.CompilerServices.Unsafe.dll => 104
	i64 u0x609f4b7b63d802d4, ; 421: lib_Microsoft.Extensions.DependencyInjection.dll.so => 205
	i64 u0x60cd4e33d7e60134, ; 422: Xamarin.KotlinX.Coroutines.Core.Jvm => 326
	i64 u0x60f62d786afcf130, ; 423: System.Memory => 64
	i64 u0x61ab8584adb538d6, ; 424: Masa.Blazor.dll => 183
	i64 u0x61bb78c89f867353, ; 425: System.IO => 58
	i64 u0x61be8d1299194243, ; 426: Microsoft.Maui.Controls.Xaml => 225
	i64 u0x61d2cba29557038f, ; 427: de/Microsoft.Maui.Controls.resources => 333
	i64 u0x61d88f399afb2f45, ; 428: lib_System.Runtime.Loader.dll.so => 112
	i64 u0x622eef6f9e59068d, ; 429: System.Private.CoreLib => 177
	i64 u0x639fb99a7bef11de, ; 430: Xamarin.AndroidX.Navigation.Runtime.Android.dll => 293
	i64 u0x63d5e3aa4ef9b931, ; 431: Xamarin.KotlinX.Coroutines.Android.dll => 324
	i64 u0x63f1f6883c1e23c2, ; 432: lib_System.Collections.Immutable.dll.so => 9
	i64 u0x6400f68068c1e9f1, ; 433: Xamarin.Google.Android.Material.dll => 316
	i64 u0x640e3b14dbd325c2, ; 434: System.Security.Cryptography.Algorithms.dll => 122
	i64 u0x64587004560099b9, ; 435: System.Reflection => 100
	i64 u0x64b1529a438a3c45, ; 436: lib_System.Runtime.Handles.dll.so => 107
	i64 u0x64b61dd9da8a4d57, ; 437: System.Net.ServerSentEvents.dll => 76
	i64 u0x6565fba2cd8f235b, ; 438: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx => 285
	i64 u0x65ecac39144dd3cc, ; 439: Microsoft.Maui.Controls.dll => 224
	i64 u0x65ece51227bfa724, ; 440: lib_System.Runtime.Numerics.dll.so => 113
	i64 u0x661722438787b57f, ; 441: Xamarin.AndroidX.Annotation.Jvm.dll => 245
	i64 u0x6679b2337ee6b22a, ; 442: lib_System.IO.FileSystem.Primitives.dll.so => 49
	i64 u0x667c66a03dd97d40, ; 443: System.Linq.AsyncEnumerable.dll => 59
	i64 u0x6692e924eade1b29, ; 444: lib_System.Console.dll.so => 20
	i64 u0x66a4e5c6a3fb0bae, ; 445: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll.so => 284
	i64 u0x66d13304ce1a3efa, ; 446: Xamarin.AndroidX.CursorAdapter => 262
	i64 u0x674303f65d8fad6f, ; 447: lib_System.Net.Quic.dll.so => 73
	i64 u0x6756ca4cad62e9d6, ; 448: lib_Xamarin.AndroidX.ConstraintLayout.Core.dll.so => 257
	i64 u0x677805927d4b2e9f, ; 449: Microsoft.AspNetCore.Components.Authorization.dll => 187
	i64 u0x67c0802770244408, ; 450: System.Windows.dll => 158
	i64 u0x68100b69286e27cd, ; 451: lib_System.Formats.Tar.dll.so => 39
	i64 u0x684d6ec54c1b6392, ; 452: lib_FinanceTracker.Core.dll.so => 363
	i64 u0x68558ec653afa616, ; 453: lib-da-Microsoft.Maui.Controls.resources.dll.so => 332
	i64 u0x6857d56b8e8b4bb6, ; 454: lib_Microsoft.AspNetCore.Metadata.dll.so => 192
	i64 u0x6872ec7a2e36b1ac, ; 455: System.Drawing.Primitives.dll => 35
	i64 u0x68fbbbe2eb455198, ; 456: System.Formats.Asn1 => 38
	i64 u0x69063fc0ba8e6bdd, ; 457: he/Microsoft.Maui.Controls.resources.dll => 338
	i64 u0x699dffb2427a2d71, ; 458: SQLitePCLRaw.lib.e_sqlite3.android.dll => 234
	i64 u0x6a4d7577b2317255, ; 459: System.Runtime.InteropServices.dll => 110
	i64 u0x6ace3b74b15ee4a4, ; 460: nb/Microsoft.Maui.Controls.resources => 347
	i64 u0x6afcedb171067e2b, ; 461: System.Core.dll => 21
	i64 u0x6bef98e124147c24, ; 462: Xamarin.Jetbrains.Annotations => 321
	i64 u0x6c7b715e10089bd8, ; 463: lib_FinanceTracker.Web.dll.so => 366
	i64 u0x6ce874bff138ce2b, ; 464: Xamarin.AndroidX.Lifecycle.ViewModel.dll => 283
	i64 u0x6d12bfaa99c72b1f, ; 465: lib_Microsoft.Maui.Graphics.dll.so => 228
	i64 u0x6d70755158ca866e, ; 466: lib_System.ComponentModel.EventBasedAsync.dll.so => 15
	i64 u0x6d79993361e10ef2, ; 467: Microsoft.Extensions.Primitives => 221
	i64 u0x6d7eeca99577fc8b, ; 468: lib_System.Net.WebProxy.dll.so => 81
	i64 u0x6d8515b19946b6a2, ; 469: System.Net.WebProxy.dll => 81
	i64 u0x6d86d56b84c8eb71, ; 470: lib_Xamarin.AndroidX.CursorAdapter.dll.so => 262
	i64 u0x6d9bea6b3e895cf7, ; 471: Microsoft.Extensions.Primitives.dll => 221
	i64 u0x6e25a02c3833319a, ; 472: lib_Xamarin.AndroidX.Navigation.Fragment.dll.so => 291
	i64 u0x6e79c6bd8627412a, ; 473: Xamarin.AndroidX.SavedState.SavedState.Ktx => 300
	i64 u0x6e838d9a2a6f6c9e, ; 474: lib_System.ValueTuple.dll.so => 155
	i64 u0x6e9965ce1095e60a, ; 475: lib_System.Core.dll.so => 21
	i64 u0x6fd2265da78b93a4, ; 476: lib_Microsoft.Maui.dll.so => 226
	i64 u0x6fdfc7de82c33008, ; 477: cs/Microsoft.Maui.Controls.resources => 331
	i64 u0x6ffc4967cc47ba57, ; 478: System.IO.FileSystem.Watcher.dll => 50
	i64 u0x701cd46a1c25a5fe, ; 479: System.IO.FileSystem.dll => 51
	i64 u0x70d67fb826a6921f, ; 480: Microsoft.Extensions.Validation.dll => 222
	i64 u0x70e99f48c05cb921, ; 481: tr/Microsoft.Maui.Controls.resources.dll => 357
	i64 u0x70fd3deda22442d2, ; 482: lib-nb-Microsoft.Maui.Controls.resources.dll.so => 347
	i64 u0x71485e7ffdb4b958, ; 483: System.Reflection.Extensions => 96
	i64 u0x7162a2fce67a945f, ; 484: lib_Xamarin.Android.Glide.Annotations.dll.so => 238
	i64 u0x717530326f808838, ; 485: lib_Microsoft.Extensions.Diagnostics.Abstractions.dll.so => 209
	i64 u0x71a495ea3761dde8, ; 486: lib-it-Microsoft.Maui.Controls.resources.dll.so => 343
	i64 u0x71ad672adbe48f35, ; 487: System.ComponentModel.Primitives.dll => 16
	i64 u0x720f102581a4a5c8, ; 488: Xamarin.AndroidX.Core.ViewTree => 261
	i64 u0x725f5a9e82a45c81, ; 489: System.Security.Cryptography.Encoding => 125
	i64 u0x72ad1f5f24144a92, ; 490: lib_DeepCloner.Core.dll.so => 179
	i64 u0x72b1fb4109e08d7b, ; 491: lib-hr-Microsoft.Maui.Controls.resources.dll.so => 340
	i64 u0x72e0300099accce1, ; 492: System.Xml.XPath.XDocument => 163
	i64 u0x7308a71876ed34d3, ; 493: FinanceTracker.Web.dll => 366
	i64 u0x730bfb248998f67a, ; 494: System.IO.Compression.ZipFile => 45
	i64 u0x732b2d67b9e5c47b, ; 495: Xamarin.Google.ErrorProne.Annotations.dll => 319
	i64 u0x734b76fdc0dc05bb, ; 496: lib_GoogleGson.dll.so => 182
	i64 u0x73a6be34e822f9d1, ; 497: lib_System.Runtime.Serialization.dll.so => 118
	i64 u0x73e4ce94e2eb6ffc, ; 498: lib_System.Memory.dll.so => 64
	i64 u0x73f2645914262879, ; 499: lib_Microsoft.EntityFrameworkCore.Sqlite.dll.so => 197
	i64 u0x743a1eccf080489a, ; 500: WindowsBase.dll => 169
	i64 u0x746cf89b511b4d40, ; 501: lib_Microsoft.Extensions.Diagnostics.dll.so => 208
	i64 u0x755a91767330b3d4, ; 502: lib_Microsoft.Extensions.Configuration.dll.so => 200
	i64 u0x75c326eb821b85c4, ; 503: lib_System.ComponentModel.DataAnnotations.dll.so => 14
	i64 u0x76012e7334db86e5, ; 504: lib_Xamarin.AndroidX.SavedState.dll.so => 298
	i64 u0x76ca07b878f44da0, ; 505: System.Runtime.Numerics.dll => 113
	i64 u0x7736c8a96e51a061, ; 506: lib_Xamarin.AndroidX.Annotation.Jvm.dll.so => 245
	i64 u0x778a805e625329ef, ; 507: System.Linq.Parallel => 61
	i64 u0x779f67ad3b8efbd5, ; 508: Microsoft.Extensions.Configuration.Json.dll => 204
	i64 u0x77d9074d8f33a303, ; 509: lib_System.Net.ServerSentEvents.dll.so => 76
	i64 u0x77f8a4acc2fdc449, ; 510: System.Security.Cryptography.Cng.dll => 123
	i64 u0x780bc73597a503a9, ; 511: lib-ms-Microsoft.Maui.Controls.resources.dll.so => 346
	i64 u0x782c5d8eb99ff201, ; 512: lib_Microsoft.VisualBasic.Core.dll.so => 2
	i64 u0x783606d1e53e7a1a, ; 513: th/Microsoft.Maui.Controls.resources.dll => 356
	i64 u0x78a45e51311409b6, ; 514: Xamarin.AndroidX.Fragment.dll => 270
	i64 u0x78ed4ab8f9d800a1, ; 515: Xamarin.AndroidX.Lifecycle.ViewModel => 283
	i64 u0x7a5207a7c82d30b4, ; 516: lib_Xamarin.JSpecify.dll.so => 322
	i64 u0x7a71889545dcdb00, ; 517: lib_Microsoft.AspNetCore.Components.WebView.dll.so => 190
	i64 u0x7a7e7eddf79c5d26, ; 518: lib_Xamarin.AndroidX.Lifecycle.ViewModel.dll.so => 283
	i64 u0x7a9a57d43b0845fa, ; 519: System.AppContext => 6
	i64 u0x7ad0f4f1e5d08183, ; 520: Xamarin.AndroidX.Collection.dll => 252
	i64 u0x7adb8da2ac89b647, ; 521: fi/Microsoft.Maui.Controls.resources.dll => 336
	i64 u0x7b13d9eaa944ade8, ; 522: Xamarin.AndroidX.DynamicAnimation.dll => 266
	i64 u0x7b150145c0a9058c, ; 523: Microsoft.Data.Sqlite => 193
	i64 u0x7bef86a4335c4870, ; 524: System.ComponentModel.TypeConverter => 17
	i64 u0x7c0820144cd34d6a, ; 525: sk/Microsoft.Maui.Controls.resources.dll => 354
	i64 u0x7c2a0bd1e0f988fc, ; 526: lib-de-Microsoft.Maui.Controls.resources.dll.so => 333
	i64 u0x7c41d387501568ba, ; 527: System.Net.WebClient.dll => 79
	i64 u0x7c482cd79bd24b13, ; 528: lib_Xamarin.AndroidX.ConstraintLayout.dll.so => 256
	i64 u0x7c4867f3cb880d2f, ; 529: Microsoft.AspNetCore.Metadata => 192
	i64 u0x7c60acf6404e96b6, ; 530: Xamarin.AndroidX.Navigation.Common.Android.dll => 290
	i64 u0x7cd2ec8eaf5241cd, ; 531: System.Security.dll => 133
	i64 u0x7cf9ae50dd350622, ; 532: Xamarin.Jetbrains.Annotations.dll => 321
	i64 u0x7d649b75d580bb42, ; 533: ms/Microsoft.Maui.Controls.resources.dll => 346
	i64 u0x7d8b5821548f89e7, ; 534: Microsoft.AspNetCore.Components.Forms => 188
	i64 u0x7d8ee2bdc8e3aad1, ; 535: System.Numerics.Vectors => 85
	i64 u0x7df5df8db8eaa6ac, ; 536: Microsoft.Extensions.Logging.Debug => 218
	i64 u0x7dfc3d6d9d8d7b70, ; 537: System.Collections => 12
	i64 u0x7e2e564fa2f76c65, ; 538: lib_System.Diagnostics.Tracing.dll.so => 34
	i64 u0x7e302e110e1e1346, ; 539: lib_System.Security.Claims.dll.so => 121
	i64 u0x7e4465b3f78ad8d0, ; 540: Xamarin.KotlinX.Serialization.Core.dll => 327
	i64 u0x7e571cad5915e6c3, ; 541: lib_Xamarin.AndroidX.Lifecycle.Process.dll.so => 278
	i64 u0x7e6ac99e4e8df72f, ; 542: System.IO.Hashing => 176
	i64 u0x7e6b1ca712437d7d, ; 543: Xamarin.AndroidX.Emoji2.ViewsHelper => 268
	i64 u0x7e946809d6008ef2, ; 544: lib_System.ObjectModel.dll.so => 87
	i64 u0x7ea0272c1b4a9635, ; 545: lib_Xamarin.Android.Glide.dll.so => 237
	i64 u0x7ecc13347c8fd849, ; 546: lib_System.ComponentModel.dll.so => 18
	i64 u0x7f00ddd9b9ca5a13, ; 547: Xamarin.AndroidX.ViewPager.dll => 311
	i64 u0x7f9351cd44b1273f, ; 548: Microsoft.Extensions.Configuration.Abstractions => 201
	i64 u0x7fbd557c99b3ce6f, ; 549: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.dll.so => 276
	i64 u0x8076a9a44a2ca331, ; 550: System.Net.Quic => 73
	i64 u0x80da183a87731838, ; 551: System.Reflection.Metadata => 97
	i64 u0x80fa55b6d1b0be99, ; 552: SQLitePCLRaw.provider.e_sqlite3 => 235
	i64 u0x8101a73bd4533440, ; 553: Microsoft.AspNetCore.Components.Web => 189
	i64 u0x812c069d5cdecc17, ; 554: System.dll => 168
	i64 u0x81381be520a60adb, ; 555: Xamarin.AndroidX.Interpolator.dll => 272
	i64 u0x81657cec2b31e8aa, ; 556: System.Net => 84
	i64 u0x81ab745f6c0f5ce6, ; 557: zh-Hant/Microsoft.Maui.Controls.resources => 362
	i64 u0x8277f2be6b5ce05f, ; 558: Xamarin.AndroidX.AppCompat => 246
	i64 u0x828f06563b30bc50, ; 559: lib_Xamarin.AndroidX.CardView.dll.so => 251
	i64 u0x82b399cb01b531c4, ; 560: lib_System.Web.dll.so => 157
	i64 u0x82df8f5532a10c59, ; 561: lib_System.Drawing.dll.so => 36
	i64 u0x82f0b6e911d13535, ; 562: lib_System.Transactions.dll.so => 154
	i64 u0x82f6403342e12049, ; 563: uk/Microsoft.Maui.Controls.resources => 358
	i64 u0x83c14ba66c8e2b8c, ; 564: zh-Hans/Microsoft.Maui.Controls.resources => 361
	i64 u0x83de69860da6cbdd, ; 565: Microsoft.Extensions.FileProviders.Composite => 211
	i64 u0x846ce984efea52c7, ; 566: System.Threading.Tasks.Parallel.dll => 147
	i64 u0x84ae73148a4557d2, ; 567: lib_System.IO.Pipes.dll.so => 56
	i64 u0x84b01102c12a9232, ; 568: System.Runtime.Serialization.Json.dll => 115
	i64 u0x84cd5cdec0f54bcc, ; 569: lib_Microsoft.EntityFrameworkCore.Relational.dll.so => 196
	i64 u0x850c5ba0b57ce8e7, ; 570: lib_Xamarin.AndroidX.Collection.dll.so => 252
	i64 u0x851d02edd334b044, ; 571: Xamarin.AndroidX.VectorDrawable => 308
	i64 u0x85c919db62150978, ; 572: Xamarin.AndroidX.Transition.dll => 307
	i64 u0x860a73814bd8d3ce, ; 573: lib_FinanceTracker.Shared.dll.so => 365
	i64 u0x862f3b2824b81128, ; 574: FinanceTracker.App => 0
	i64 u0x8662aaeb94fef37f, ; 575: lib_System.Dynamic.Runtime.dll.so => 37
	i64 u0x86a909228dc7657b, ; 576: lib-zh-Hant-Microsoft.Maui.Controls.resources.dll.so => 362
	i64 u0x86b3e00c36b84509, ; 577: Microsoft.Extensions.Configuration.dll => 200
	i64 u0x86b62cb077ec4fd7, ; 578: System.Runtime.Serialization.Xml => 117
	i64 u0x8704193f462e892e, ; 579: lib_Microsoft.Extensions.FileSystemGlobbing.dll.so => 214
	i64 u0x8706ffb12bf3f53d, ; 580: Xamarin.AndroidX.Annotation.Experimental => 244
	i64 u0x872a5b14c18d328c, ; 581: System.ComponentModel.DataAnnotations => 14
	i64 u0x872fb9615bc2dff0, ; 582: Xamarin.Android.Glide.Annotations.dll => 238
	i64 u0x87c4b8a492b176ad, ; 583: Microsoft.EntityFrameworkCore.Abstractions => 195
	i64 u0x87c69b87d9283884, ; 584: lib_System.Threading.Thread.dll.so => 149
	i64 u0x87f6569b25707834, ; 585: System.IO.Compression.Brotli.dll => 43
	i64 u0x8842b3a5d2d3fb36, ; 586: Microsoft.Maui.Essentials => 227
	i64 u0x88926583efe7ee86, ; 587: Xamarin.AndroidX.Activity.Ktx.dll => 242
	i64 u0x88ba6bc4f7762b03, ; 588: lib_System.Reflection.dll.so => 100
	i64 u0x88bda98e0cffb7a9, ; 589: lib_Xamarin.KotlinX.Coroutines.Core.Jvm.dll.so => 326
	i64 u0x8930322c7bd8f768, ; 590: netstandard => 171
	i64 u0x897a606c9e39c75f, ; 591: lib_System.ComponentModel.Primitives.dll.so => 16
	i64 u0x898a5c6bc9e47ec1, ; 592: lib_Xamarin.AndroidX.SavedState.SavedState.Android.dll.so => 299
	i64 u0x89911a22005b92b7, ; 593: System.IO.FileSystem.DriveInfo.dll => 48
	i64 u0x89c5188089ec2cd5, ; 594: lib_System.Runtime.InteropServices.RuntimeInformation.dll.so => 109
	i64 u0x8a19e3dc71b34b2c, ; 595: System.Reflection.TypeExtensions.dll => 99
	i64 u0x8a399a706fcbce4b, ; 596: Microsoft.Extensions.Caching.Abstractions => 198
	i64 u0x8ad229ea26432ee2, ; 597: Xamarin.AndroidX.Loader => 288
	i64 u0x8b4ff5d0fdd5faa1, ; 598: lib_System.Diagnostics.DiagnosticSource.dll.so => 27
	i64 u0x8b541d476eb3774c, ; 599: System.Security.Principal.Windows => 130
	i64 u0x8b8d01333a96d0b5, ; 600: System.Diagnostics.Process.dll => 29
	i64 u0x8b9ceca7acae3451, ; 601: lib-he-Microsoft.Maui.Controls.resources.dll.so => 338
	i64 u0x8c575135aa1ccef4, ; 602: Microsoft.Extensions.FileProviders.Abstractions => 210
	i64 u0x8cb8f612b633affb, ; 603: Xamarin.AndroidX.SavedState.SavedState.Ktx.dll => 300
	i64 u0x8cdfdb4ce85fb925, ; 604: lib_System.Security.Principal.Windows.dll.so => 130
	i64 u0x8cdfe7b8f4caa426, ; 605: System.IO.Compression.FileSystem => 44
	i64 u0x8d0f420977c2c1c7, ; 606: Xamarin.AndroidX.CursorAdapter.dll => 262
	i64 u0x8d52a25632e81824, ; 607: Microsoft.EntityFrameworkCore.Sqlite.dll => 197
	i64 u0x8d52f7ea2796c531, ; 608: Xamarin.AndroidX.Emoji2.dll => 267
	i64 u0x8d7b8ab4b3310ead, ; 609: System.Threading => 152
	i64 u0x8da188285aadfe8e, ; 610: System.Collections.Concurrent => 8
	i64 u0x8e8f269ad1e1ff94, ; 611: lib_Xamarin.AndroidX.Tracing.Tracing.Android.dll.so => 306
	i64 u0x8ed807bfe9858dfc, ; 612: Xamarin.AndroidX.Navigation.Common => 289
	i64 u0x8ee08b8194a30f48, ; 613: lib-hi-Microsoft.Maui.Controls.resources.dll.so => 339
	i64 u0x8ef7601039857a44, ; 614: lib-ro-Microsoft.Maui.Controls.resources.dll.so => 352
	i64 u0x8ef9414937d93a0a, ; 615: SQLitePCLRaw.core.dll => 233
	i64 u0x8f32c6f611f6ffab, ; 616: pt/Microsoft.Maui.Controls.resources.dll => 351
	i64 u0x8f44b45eb046bbd1, ; 617: System.ServiceModel.Web.dll => 134
	i64 u0x8f8829d21c8985a4, ; 618: lib-pt-BR-Microsoft.Maui.Controls.resources.dll.so => 350
	i64 u0x8fbf5b0114c6dcef, ; 619: System.Globalization.dll => 42
	i64 u0x8fcc8c2a81f3d9e7, ; 620: Xamarin.KotlinX.Serialization.Core => 327
	i64 u0x8fd27d934d7b3a55, ; 621: SQLitePCLRaw.core => 233
	i64 u0x90263f8448b8f572, ; 622: lib_System.Diagnostics.TraceSource.dll.so => 33
	i64 u0x903101b46fb73a04, ; 623: _Microsoft.Android.Resource.Designer => 367
	i64 u0x90393bd4865292f3, ; 624: lib_System.IO.Compression.dll.so => 46
	i64 u0x905e2b8e7ae91ae6, ; 625: System.Threading.Tasks.Extensions.dll => 146
	i64 u0x90634f86c5ebe2b5, ; 626: Xamarin.AndroidX.Lifecycle.ViewModel.Android => 284
	i64 u0x907b636704ad79ef, ; 627: lib_Microsoft.Maui.Controls.Xaml.dll.so => 225
	i64 u0x90e9efbfd68593e0, ; 628: lib_Xamarin.AndroidX.Lifecycle.LiveData.dll.so => 275
	i64 u0x91418dc638b29e68, ; 629: lib_Xamarin.AndroidX.CustomView.dll.so => 263
	i64 u0x914647982e998267, ; 630: Microsoft.Extensions.Configuration.Json => 204
	i64 u0x9157bd523cd7ed36, ; 631: lib_System.Text.Json.dll.so => 140
	i64 u0x91a74f07b30d37e2, ; 632: System.Linq.dll => 63
	i64 u0x91cb86ea3b17111d, ; 633: System.ServiceModel.Web => 134
	i64 u0x91fa41a87223399f, ; 634: ca/Microsoft.Maui.Controls.resources.dll => 330
	i64 u0x92054e486c0c7ea7, ; 635: System.IO.FileSystem.DriveInfo => 48
	i64 u0x928614058c40c4cd, ; 636: lib_System.Xml.XPath.XDocument.dll.so => 163
	i64 u0x92b138fffca2b01e, ; 637: lib_Xamarin.AndroidX.Arch.Core.Runtime.dll.so => 249
	i64 u0x92dfc2bfc6c6a888, ; 638: Xamarin.AndroidX.Lifecycle.LiveData => 275
	i64 u0x933da2c779423d68, ; 639: Xamarin.Android.Glide.Annotations => 238
	i64 u0x9388aad9b7ae40ce, ; 640: lib_Xamarin.AndroidX.Lifecycle.Common.dll.so => 273
	i64 u0x93cfa73ab28d6e35, ; 641: ms/Microsoft.Maui.Controls.resources => 346
	i64 u0x941c00d21e5c0679, ; 642: lib_Xamarin.AndroidX.Transition.dll.so => 307
	i64 u0x944077d8ca3c6580, ; 643: System.IO.Compression.dll => 46
	i64 u0x948cffedc8ed7960, ; 644: System.Xml => 167
	i64 u0x94bbeab0d4764588, ; 645: System.IO.Hashing.dll => 176
	i64 u0x94c8990839c4bdb1, ; 646: lib_Xamarin.AndroidX.Interpolator.dll.so => 272
	i64 u0x967fc325e09bfa8c, ; 647: es/Microsoft.Maui.Controls.resources => 335
	i64 u0x9686161486d34b81, ; 648: lib_Xamarin.AndroidX.ExifInterface.dll.so => 269
	i64 u0x9732d8dbddea3d9a, ; 649: id/Microsoft.Maui.Controls.resources => 342
	i64 u0x978be80e5210d31b, ; 650: Microsoft.Maui.Graphics.dll => 228
	i64 u0x97b8c771ea3e4220, ; 651: System.ComponentModel.dll => 18
	i64 u0x97e144c9d3c6976e, ; 652: System.Collections.Concurrent.dll => 8
	i64 u0x984184e3c70d4419, ; 653: GoogleGson => 182
	i64 u0x9843944103683dd3, ; 654: Xamarin.AndroidX.Core.Core.Ktx => 260
	i64 u0x98b05cc81e6f333c, ; 655: Xamarin.AndroidX.SavedState.SavedState.Android.dll => 299
	i64 u0x98d720cc4597562c, ; 656: System.Security.Cryptography.OpenSsl => 126
	i64 u0x991d510397f92d9d, ; 657: System.Linq.Expressions => 60
	i64 u0x996ceeb8a3da3d67, ; 658: System.Threading.Overlapped.dll => 144
	i64 u0x99a00ca5270c6878, ; 659: Xamarin.AndroidX.Navigation.Runtime => 292
	i64 u0x99cdc6d1f2d3a72f, ; 660: ko/Microsoft.Maui.Controls.resources.dll => 345
	i64 u0x9a01b1da98b6ee10, ; 661: Xamarin.AndroidX.Lifecycle.Runtime.dll => 279
	i64 u0x9a5ccc274fd6e6ee, ; 662: Jsr305Binding.dll => 317
	i64 u0x9acfd25e735d5594, ; 663: lib_Npgsql.dll.so => 229
	i64 u0x9ae6940b11c02876, ; 664: lib_Xamarin.AndroidX.Window.dll.so => 313
	i64 u0x9b211a749105beac, ; 665: System.Transactions.Local => 153
	i64 u0x9b8734714671022d, ; 666: System.Threading.Tasks.Dataflow.dll => 145
	i64 u0x9bc6aea27fbf034f, ; 667: lib_Xamarin.KotlinX.Coroutines.Core.dll.so => 325
	i64 u0x9c244ac7cda32d26, ; 668: System.Security.Cryptography.X509Certificates.dll => 128
	i64 u0x9c465f280cf43733, ; 669: lib_Xamarin.KotlinX.Coroutines.Android.dll.so => 324
	i64 u0x9c8f6872beab6408, ; 670: System.Xml.XPath.XDocument.dll => 163
	i64 u0x9ce01cf91101ae23, ; 671: System.Xml.XmlDocument => 165
	i64 u0x9d128180c81d7ce6, ; 672: Xamarin.AndroidX.CustomView.PoolingContainer => 264
	i64 u0x9d5dbcf5a48583fe, ; 673: lib_Xamarin.AndroidX.Activity.dll.so => 241
	i64 u0x9d74dee1a7725f34, ; 674: Microsoft.Extensions.Configuration.Abstractions.dll => 201
	i64 u0x9dd0e195825d65c6, ; 675: lib_Xamarin.AndroidX.Navigation.Runtime.Android.dll.so => 293
	i64 u0x9e4534b6adaf6e84, ; 676: nl/Microsoft.Maui.Controls.resources => 348
	i64 u0x9e4b95dec42769f7, ; 677: System.Diagnostics.Debug.dll => 26
	i64 u0x9e7886e9d0f8c2ef, ; 678: lib_OneOf.dll.so => 231
	i64 u0x9e78e97e330a0086, ; 679: Microsoft.AspNetCore.Components.Authorization => 187
	i64 u0x9eaf1efdf6f7267e, ; 680: Xamarin.AndroidX.Navigation.Common.dll => 289
	i64 u0x9ef542cf1f78c506, ; 681: Xamarin.AndroidX.Lifecycle.LiveData.Core => 276
	i64 u0x9fbb2961ca18e5c2, ; 682: Microsoft.Extensions.FileProviders.Physical.dll => 213
	i64 u0xa00832eb975f56a8, ; 683: lib_System.Net.dll.so => 84
	i64 u0xa0ad78236b7b267f, ; 684: Xamarin.AndroidX.Window => 313
	i64 u0xa0d8259f4cc284ec, ; 685: lib_System.Security.Cryptography.dll.so => 129
	i64 u0xa0e17ca50c77a225, ; 686: lib_Xamarin.Google.Crypto.Tink.Android.dll.so => 318
	i64 u0xa0ff9b3e34d92f11, ; 687: lib_System.Resources.Writer.dll.so => 103
	i64 u0xa12fbfb4da97d9f3, ; 688: System.Threading.Timer.dll => 151
	i64 u0xa13f33d9a41bee22, ; 689: FluentValidation.dll => 180
	i64 u0xa1440773ee9d341e, ; 690: Xamarin.Google.Android.Material => 316
	i64 u0xa18c39c44cdc3465, ; 691: Xamarin.AndroidX.Window.WindowCore => 314
	i64 u0xa1b9d7c27f47219f, ; 692: Xamarin.AndroidX.Navigation.UI.dll => 294
	i64 u0xa1cfec8d4a8d7c32, ; 693: Npgsql.EntityFrameworkCore.PostgreSQL.dll => 230
	i64 u0xa2572680829d2c7c, ; 694: System.IO.Pipelines.dll => 54
	i64 u0xa26597e57ee9c7f6, ; 695: System.Xml.XmlDocument.dll => 165
	i64 u0xa2c96247c15c7b0e, ; 696: FinanceTracker.Infrastructure.dll => 364
	i64 u0xa308401900e5bed3, ; 697: lib_mscorlib.dll.so => 170
	i64 u0xa376c45ce4849e2a, ; 698: Masa.Blazor.MobileComponents.dll => 184
	i64 u0xa395572e7da6c99d, ; 699: lib_System.Security.dll.so => 133
	i64 u0xa3b8104115a36bf6, ; 700: lib_Microsoft.Extensions.FileProviders.Embedded.dll.so => 212
	i64 u0xa3e683f24b43af6f, ; 701: System.Dynamic.Runtime.dll => 37
	i64 u0xa4145becdee3dc4f, ; 702: Xamarin.AndroidX.VectorDrawable.Animated => 309
	i64 u0xa46aa1eaa214539b, ; 703: ko/Microsoft.Maui.Controls.resources => 345
	i64 u0xa4a372eecb9e4df0, ; 704: Microsoft.Extensions.Diagnostics => 208
	i64 u0xa4e62983cf1e3674, ; 705: Microsoft.AspNetCore.Components.Forms.dll => 188
	i64 u0xa4edc8f2ceae241a, ; 706: System.Data.Common.dll => 22
	i64 u0xa5494f40f128ce6a, ; 707: System.Runtime.Serialization.Formatters.dll => 114
	i64 u0xa54b74df83dce92b, ; 708: System.Reflection.DispatchProxy => 92
	i64 u0xa5b7152421ed6d98, ; 709: lib_System.IO.FileSystem.Watcher.dll.so => 50
	i64 u0xa5c3844f17b822db, ; 710: lib_System.Linq.Parallel.dll.so => 61
	i64 u0xa5ce5c755bde8cb8, ; 711: lib_System.Security.Cryptography.Csp.dll.so => 124
	i64 u0xa5e599d1e0524750, ; 712: System.Numerics.Vectors.dll => 85
	i64 u0xa5f1ba49b85dd355, ; 713: System.Security.Cryptography.dll => 129
	i64 u0xa61975a5a37873ea, ; 714: lib_System.Xml.XmlSerializer.dll.so => 166
	i64 u0xa6593e21584384d2, ; 715: lib_Jsr305Binding.dll.so => 317
	i64 u0xa66cbee0130865f7, ; 716: lib_WindowsBase.dll.so => 169
	i64 u0xa67dbee13e1df9ca, ; 717: Xamarin.AndroidX.SavedState.dll => 298
	i64 u0xa684b098dd27b296, ; 718: lib_Xamarin.AndroidX.Security.SecurityCrypto.dll.so => 301
	i64 u0xa68a420042bb9b1f, ; 719: Xamarin.AndroidX.DrawerLayout.dll => 265
	i64 u0xa6d26156d1cacc7c, ; 720: Xamarin.Android.Glide.dll => 237
	i64 u0xa75386b5cb9595aa, ; 721: Xamarin.AndroidX.Lifecycle.Runtime.Android => 280
	i64 u0xa763fbb98df8d9fb, ; 722: lib_Microsoft.Win32.Primitives.dll.so => 4
	i64 u0xa78ce3745383236a, ; 723: Xamarin.AndroidX.Lifecycle.Common.Jvm => 274
	i64 u0xa7c31b56b4dc7b33, ; 724: hu/Microsoft.Maui.Controls.resources => 341
	i64 u0xa7eab29ed44b4e7a, ; 725: Mono.Android.Export => 173
	i64 u0xa8195217cbf017b7, ; 726: Microsoft.VisualBasic.Core => 2
	i64 u0xa82fd211eef00a5b, ; 727: Microsoft.Extensions.FileProviders.Physical => 213
	i64 u0xa859a95830f367ff, ; 728: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll.so => 285
	i64 u0xa87275d3cfb55897, ; 729: FinanceTracker.Core => 363
	i64 u0xa8b52f21e0dbe690, ; 730: System.Runtime.Serialization.dll => 118
	i64 u0xa8ee4ed7de2efaee, ; 731: Xamarin.AndroidX.Annotation.dll => 243
	i64 u0xa95590e7c57438a4, ; 732: System.Configuration => 19
	i64 u0xaa2219c8e3449ff5, ; 733: Microsoft.Extensions.Logging.Abstractions => 217
	i64 u0xaa443ac34067eeef, ; 734: System.Private.Xml.dll => 91
	i64 u0xaa52de307ef5d1dd, ; 735: System.Net.Http => 66
	i64 u0xaa9a7b0214a5cc5c, ; 736: System.Diagnostics.StackTrace.dll => 30
	i64 u0xaaaf86367285a918, ; 737: Microsoft.Extensions.DependencyInjection.Abstractions.dll => 206
	i64 u0xaaf84bb3f052a265, ; 738: el/Microsoft.Maui.Controls.resources => 334
	i64 u0xab9af77b5b67a0b8, ; 739: Xamarin.AndroidX.ConstraintLayout.Core => 257
	i64 u0xab9c1b2687d86b0b, ; 740: lib_System.Linq.Expressions.dll.so => 60
	i64 u0xac2af3fa195a15ce, ; 741: System.Runtime.Numerics => 113
	i64 u0xac5376a2a538dc10, ; 742: Xamarin.AndroidX.Lifecycle.LiveData.Core.dll => 276
	i64 u0xac5acae88f60357e, ; 743: System.Diagnostics.Tools.dll => 32
	i64 u0xac79c7e46047ad98, ; 744: System.Security.Principal.Windows.dll => 130
	i64 u0xac98d31068e24591, ; 745: System.Xml.XDocument => 162
	i64 u0xacd46e002c3ccb97, ; 746: ro/Microsoft.Maui.Controls.resources => 352
	i64 u0xacdd9e4180d56dda, ; 747: Xamarin.AndroidX.Concurrent.Futures => 255
	i64 u0xacf42eea7ef9cd12, ; 748: System.Threading.Channels => 143
	i64 u0xad89c07347f1bad6, ; 749: nl/Microsoft.Maui.Controls.resources.dll => 348
	i64 u0xadbb53caf78a79d2, ; 750: System.Web.HttpUtility => 156
	i64 u0xadc90ab061a9e6e4, ; 751: System.ComponentModel.TypeConverter.dll => 17
	i64 u0xadca1b9030b9317e, ; 752: Xamarin.AndroidX.Collection.Ktx => 254
	i64 u0xadd8eda2edf396ad, ; 753: Xamarin.Android.Glide.GifDecoder => 240
	i64 u0xadf4cf30debbeb9a, ; 754: System.Net.ServicePoint.dll => 77
	i64 u0xadf511667bef3595, ; 755: System.Net.Security => 75
	i64 u0xae0aaa94fdcfce0f, ; 756: System.ComponentModel.EventBasedAsync.dll => 15
	i64 u0xae282bcd03739de7, ; 757: Java.Interop => 172
	i64 u0xae53579c90db1107, ; 758: System.ObjectModel.dll => 87
	i64 u0xaf12fb8133ac3fbb, ; 759: Microsoft.EntityFrameworkCore.Sqlite => 197
	i64 u0xaf732d0b2193b8f5, ; 760: System.Security.Cryptography.OpenSsl.dll => 126
	i64 u0xafdb94dbccd9d11c, ; 761: Xamarin.AndroidX.Lifecycle.LiveData.dll => 275
	i64 u0xafe29f45095518e7, ; 762: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll.so => 286
	i64 u0xb03ae931fb25607e, ; 763: Xamarin.AndroidX.ConstraintLayout => 256
	i64 u0xb05cc42cd94c6d9d, ; 764: lib-sv-Microsoft.Maui.Controls.resources.dll.so => 355
	i64 u0xb0a3b7be598a5333, ; 765: FinanceTracker.App.dll => 0
	i64 u0xb0ac21bec8f428c5, ; 766: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android.dll => 282
	i64 u0xb0bb43dc52ea59f9, ; 767: System.Diagnostics.Tracing.dll => 34
	i64 u0xb1ccbf6243328d1c, ; 768: Microsoft.AspNetCore.Components => 186
	i64 u0xb1dd05401aa8ee63, ; 769: System.Security.AccessControl => 120
	i64 u0xb220631954820169, ; 770: System.Text.RegularExpressions => 141
	i64 u0xb222353a6921410c, ; 771: FinanceTracker.Shared.dll => 365
	i64 u0xb2376e1dbf8b4ed7, ; 772: System.Security.Cryptography.Csp => 124
	i64 u0xb2a1959fe95c5402, ; 773: lib_System.Runtime.InteropServices.JavaScript.dll.so => 108
	i64 u0xb2a3f67f3bf29fce, ; 774: da/Microsoft.Maui.Controls.resources => 332
	i64 u0xb3874072ee0ecf8c, ; 775: Xamarin.AndroidX.VectorDrawable.Animated.dll => 309
	i64 u0xb3d102ae4822e0ce, ; 776: OneOf => 231
	i64 u0xb3f0a0fcda8d3ebc, ; 777: Xamarin.AndroidX.CardView => 251
	i64 u0xb46be1aa6d4fff93, ; 778: hi/Microsoft.Maui.Controls.resources => 339
	i64 u0xb477491be13109d8, ; 779: ar/Microsoft.Maui.Controls.resources => 329
	i64 u0xb4bd7015ecee9d86, ; 780: System.IO.Pipelines => 54
	i64 u0xb4c53d9749c5f226, ; 781: lib_System.IO.FileSystem.AccessControl.dll.so => 47
	i64 u0xb4ff710863453fda, ; 782: System.Diagnostics.FileVersionInfo.dll => 28
	i64 u0xb54092076b15e062, ; 783: System.Threading.AccessControl => 142
	i64 u0xb5c38bf497a4cfe2, ; 784: lib_System.Threading.Tasks.dll.so => 148
	i64 u0xb5c7fcdafbc67ee4, ; 785: Microsoft.Extensions.Logging.Abstractions.dll => 217
	i64 u0xb5ea31d5244c6626, ; 786: System.Threading.ThreadPool.dll => 150
	i64 u0xb7212c4683a94afe, ; 787: System.Drawing.Primitives => 35
	i64 u0xb7b7753d1f319409, ; 788: sv/Microsoft.Maui.Controls.resources => 355
	i64 u0xb81a2c6e0aee50fe, ; 789: lib_System.Private.CoreLib.dll.so => 177
	i64 u0xb8c60af47c08d4da, ; 790: System.Net.ServicePoint => 77
	i64 u0xb8e68d20aad91196, ; 791: lib_System.Xml.XPath.dll.so => 164
	i64 u0xb9185c33a1643eed, ; 792: Microsoft.CSharp.dll => 1
	i64 u0xb960d6b2200ba320, ; 793: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll => 287
	i64 u0xb9b8001adf4ed7cc, ; 794: lib_Xamarin.AndroidX.SlidingPaneLayout.dll.so => 302
	i64 u0xb9f64d3b230def68, ; 795: lib-pt-Microsoft.Maui.Controls.resources.dll.so => 351
	i64 u0xb9fc3c8a556e3691, ; 796: ja/Microsoft.Maui.Controls.resources => 344
	i64 u0xba4670aa94a2b3c6, ; 797: lib_System.Xml.XDocument.dll.so => 162
	i64 u0xba48785529705af9, ; 798: System.Collections.dll => 12
	i64 u0xba965b8c86359996, ; 799: lib_System.Windows.dll.so => 158
	i64 u0xbaf762c4825c14e9, ; 800: Microsoft.AspNetCore.Components.WebView => 190
	i64 u0xbb286883bc35db36, ; 801: System.Transactions.dll => 154
	i64 u0xbb31ecca4a6a4bc7, ; 802: lib_BemIt.dll.so => 178
	i64 u0xbb65706fde942ce3, ; 803: System.Net.Sockets => 78
	i64 u0xbba28979413cad9e, ; 804: lib_System.Runtime.CompilerServices.VisualC.dll.so => 105
	i64 u0xbba6aa1ba9f9c1fe, ; 805: Masa.Blazor => 183
	i64 u0xbbd180354b67271a, ; 806: System.Runtime.Serialization.Formatters => 114
	i64 u0xbc22a245dab70cb4, ; 807: lib_SQLitePCLRaw.provider.e_sqlite3.dll.so => 235
	i64 u0xbc260cdba33291a3, ; 808: Xamarin.AndroidX.Arch.Core.Common.dll => 248
	i64 u0xbc3c4e8dffea9d4e, ; 809: Microsoft.AspNetCore.Metadata.dll => 192
	i64 u0xbcd36316d29f27b4, ; 810: lib_Microsoft.AspNetCore.Authorization.dll.so => 185
	i64 u0xbd0e2c0d55246576, ; 811: System.Net.Http.dll => 66
	i64 u0xbd3fbd85b9e1cb29, ; 812: lib_System.Net.HttpListener.dll.so => 67
	i64 u0xbd437a2cdb333d0d, ; 813: Xamarin.AndroidX.ViewPager2 => 312
	i64 u0xbd4f572d2bd0a789, ; 814: System.IO.Compression.ZipFile.dll => 45
	i64 u0xbd5d0b88d3d647a5, ; 815: lib_Xamarin.AndroidX.Browser.dll.so => 250
	i64 u0xbd877b14d0b56392, ; 816: System.Runtime.Intrinsics.dll => 111
	i64 u0xbe65a49036345cf4, ; 817: lib_System.Buffers.dll.so => 7
	i64 u0xbee38d4a88835966, ; 818: Xamarin.AndroidX.AppCompat.AppCompatResources => 247
	i64 u0xbef9919db45b4ca7, ; 819: System.IO.Pipes.AccessControl => 55
	i64 u0xbf0fa68611139208, ; 820: lib_Xamarin.AndroidX.Annotation.dll.so => 243
	i64 u0xbf900f67b9463b03, ; 821: FinanceTracker.Shared => 365
	i64 u0xbfc1e1fb3095f2b3, ; 822: lib_System.Net.Http.Json.dll.so => 65
	i64 u0xc040a4ab55817f58, ; 823: ar/Microsoft.Maui.Controls.resources.dll => 329
	i64 u0xc07cadab29efeba0, ; 824: Xamarin.AndroidX.Core.Core.Ktx.dll => 260
	i64 u0xc0d928351ab5ca77, ; 825: System.Console.dll => 20
	i64 u0xc0f5a221a9383aea, ; 826: System.Runtime.Intrinsics => 111
	i64 u0xc111030af54d7191, ; 827: System.Resources.Writer => 103
	i64 u0xc12b8b3afa48329c, ; 828: lib_System.Linq.dll.so => 63
	i64 u0xc183ca0b74453aa9, ; 829: lib_System.Threading.Tasks.Dataflow.dll.so => 145
	i64 u0xc1c2cb7af77b8858, ; 830: Microsoft.EntityFrameworkCore => 194
	i64 u0xc1ebdc7e6a943450, ; 831: Microsoft.AspNetCore.Authorization.dll => 185
	i64 u0xc1ff9ae3cdb6e1e6, ; 832: Xamarin.AndroidX.Activity.dll => 241
	i64 u0xc224e49de63c5dcd, ; 833: FluentValidation.DependencyInjectionExtensions.dll => 181
	i64 u0xc26c064effb1dea9, ; 834: System.Buffers.dll => 7
	i64 u0xc28c50f32f81cc73, ; 835: ja/Microsoft.Maui.Controls.resources.dll => 344
	i64 u0xc2902f6cf5452577, ; 836: lib_Mono.Android.Export.dll.so => 173
	i64 u0xc2a3bca55b573141, ; 837: System.IO.FileSystem.Watcher => 50
	i64 u0xc2bcfec99f69365e, ; 838: Xamarin.AndroidX.ViewPager2.dll => 312
	i64 u0xc30b52815b58ac2c, ; 839: lib_System.Runtime.Serialization.Xml.dll.so => 117
	i64 u0xc3492f8f90f96ce4, ; 840: lib_Microsoft.Extensions.DependencyModel.dll.so => 207
	i64 u0xc36d7d89c652f455, ; 841: System.Threading.Overlapped => 144
	i64 u0xc396b285e59e5493, ; 842: GoogleGson.dll => 182
	i64 u0xc3c86c1e5e12f03d, ; 843: WindowsBase => 169
	i64 u0xc421b61fd853169d, ; 844: lib_System.Net.WebSockets.Client.dll.so => 82
	i64 u0xc437270f535c7856, ; 845: lib_FinanceTracker.Infrastructure.dll.so => 364
	i64 u0xc463e077917aa21d, ; 846: System.Runtime.Serialization.Json => 115
	i64 u0xc472ce300460ccb6, ; 847: Microsoft.EntityFrameworkCore.dll => 194
	i64 u0xc4d3858ed4d08512, ; 848: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll => 286
	i64 u0xc4d69851fe06342f, ; 849: lib_Microsoft.Extensions.Caching.Memory.dll.so => 199
	i64 u0xc50fded0ded1418c, ; 850: lib_System.ComponentModel.TypeConverter.dll.so => 17
	i64 u0xc519125d6bc8fb11, ; 851: lib_System.Net.Requests.dll.so => 74
	i64 u0xc5293b19e4dc230e, ; 852: Xamarin.AndroidX.Navigation.Fragment => 291
	i64 u0xc5325b2fcb37446f, ; 853: lib_System.Private.Xml.dll.so => 91
	i64 u0xc535cb9a21385d9b, ; 854: lib_Xamarin.Android.Glide.DiskLruCache.dll.so => 239
	i64 u0xc5a0f4b95a699af7, ; 855: lib_System.Private.Uri.dll.so => 89
	i64 u0xc5cdcd5b6277579e, ; 856: lib_System.Security.Cryptography.Algorithms.dll.so => 122
	i64 u0xc5ec286825cb0bf4, ; 857: Xamarin.AndroidX.Tracing.Tracing => 305
	i64 u0xc659b586d4c229e2, ; 858: Microsoft.Extensions.Configuration.FileExtensions.dll => 203
	i64 u0xc6706bc8aa7fe265, ; 859: Xamarin.AndroidX.Annotation.Jvm => 245
	i64 u0xc74d70d4aa96cef3, ; 860: Xamarin.AndroidX.Navigation.Runtime.Android => 293
	i64 u0xc7c01e7d7c93a110, ; 861: System.Text.Encoding.Extensions.dll => 137
	i64 u0xc7ce851898a4548e, ; 862: lib_System.Web.HttpUtility.dll.so => 156
	i64 u0xc809d4089d2556b2, ; 863: System.Runtime.InteropServices.JavaScript.dll => 108
	i64 u0xc858a28d9ee5a6c5, ; 864: lib_System.Collections.Specialized.dll.so => 11
	i64 u0xc8ac7c6bf1c2ec51, ; 865: System.Reflection.DispatchProxy.dll => 92
	i64 u0xc9c62c8f354ac568, ; 866: lib_System.Diagnostics.TextWriterTraceListener.dll.so => 31
	i64 u0xca3110fea81c8916, ; 867: Microsoft.AspNetCore.Components.Web.dll => 189
	i64 u0xca32340d8d54dcd5, ; 868: Microsoft.Extensions.Caching.Memory.dll => 199
	i64 u0xca3a723e7342c5b6, ; 869: lib-tr-Microsoft.Maui.Controls.resources.dll.so => 357
	i64 u0xca5801070d9fccfb, ; 870: System.Text.Encoding => 138
	i64 u0xcab3493c70141c2d, ; 871: pl/Microsoft.Maui.Controls.resources => 349
	i64 u0xcacfddc9f7c6de76, ; 872: ro/Microsoft.Maui.Controls.resources.dll => 352
	i64 u0xcadbc92899a777f0, ; 873: Xamarin.AndroidX.Startup.StartupRuntime => 303
	i64 u0xcb45618372c47127, ; 874: Microsoft.EntityFrameworkCore.Relational => 196
	i64 u0xcb6f731cbdfa3dd8, ; 875: Npgsql.EntityFrameworkCore.PostgreSQL => 230
	i64 u0xcba1cb79f45292b5, ; 876: Xamarin.Android.Glide.GifDecoder.dll => 240
	i64 u0xcbb5f80c7293e696, ; 877: lib_System.Globalization.Calendars.dll.so => 40
	i64 u0xcbd4fdd9cef4a294, ; 878: lib__Microsoft.Android.Resource.Designer.dll.so => 367
	i64 u0xcc15da1e07bbd994, ; 879: Xamarin.AndroidX.SlidingPaneLayout => 302
	i64 u0xcc2876b32ef2794c, ; 880: lib_System.Text.RegularExpressions.dll.so => 141
	i64 u0xcc5c3bb714c4561e, ; 881: Xamarin.KotlinX.Coroutines.Core.Jvm.dll => 326
	i64 u0xcc76886e09b88260, ; 882: Xamarin.KotlinX.Serialization.Core.Jvm.dll => 328
	i64 u0xcc9fa2923aa1c9ef, ; 883: System.Diagnostics.Contracts.dll => 25
	i64 u0xccae9bb73e2326bd, ; 884: lib_System.IO.Hashing.dll.so => 176
	i64 u0xccf25c4b634ccd3a, ; 885: zh-Hans/Microsoft.Maui.Controls.resources.dll => 361
	i64 u0xcd10a42808629144, ; 886: System.Net.Requests => 74
	i64 u0xcdca1b920e9f53ba, ; 887: Xamarin.AndroidX.Interpolator => 272
	i64 u0xcdd0c48b6937b21c, ; 888: Xamarin.AndroidX.SwipeRefreshLayout => 304
	i64 u0xcf23d8093f3ceadf, ; 889: System.Diagnostics.DiagnosticSource.dll => 27
	i64 u0xcf5ff6b6b2c4c382, ; 890: System.Net.Mail.dll => 68
	i64 u0xcf8fc898f98b0d34, ; 891: System.Private.Xml.Linq => 90
	i64 u0xcfcfd50998ac9247, ; 892: lib_FluentValidation.dll.so => 180
	i64 u0xd04b5f59ed596e31, ; 893: System.Reflection.Metadata.dll => 97
	i64 u0xd063299fcfc0c93f, ; 894: lib_System.Runtime.Serialization.Json.dll.so => 115
	i64 u0xd0de8a113e976700, ; 895: System.Diagnostics.TextWriterTraceListener => 31
	i64 u0xd0fc33d5ae5d4cb8, ; 896: System.Runtime.Extensions => 106
	i64 u0xd1194e1d8a8de83c, ; 897: lib_Xamarin.AndroidX.Lifecycle.Common.Jvm.dll.so => 274
	i64 u0xd12beacdfc14f696, ; 898: System.Dynamic.Runtime => 37
	i64 u0xd16fd7fb9bbcd43e, ; 899: Microsoft.Extensions.Diagnostics.Abstractions => 209
	i64 u0xd198e7ce1b6a8344, ; 900: System.Net.Quic.dll => 73
	i64 u0xd2505d8abeed6983, ; 901: lib_Microsoft.AspNetCore.Components.Web.dll.so => 189
	i64 u0xd3144156a3727ebe, ; 902: Xamarin.Google.Guava.ListenableFuture => 320
	i64 u0xd333d0af9e423810, ; 903: System.Runtime.InteropServices => 110
	i64 u0xd33a415cb4278969, ; 904: System.Security.Cryptography.Encoding.dll => 125
	i64 u0xd3426d966bb704f5, ; 905: Xamarin.AndroidX.AppCompat.AppCompatResources.dll => 247
	i64 u0xd3651b6fc3125825, ; 906: System.Private.Uri.dll => 89
	i64 u0xd373685349b1fe8b, ; 907: Microsoft.Extensions.Logging.dll => 216
	i64 u0xd3801faafafb7698, ; 908: System.Private.DataContractSerialization.dll => 88
	i64 u0xd3e4c8d6a2d5d470, ; 909: it/Microsoft.Maui.Controls.resources => 343
	i64 u0xd3edcc1f25459a50, ; 910: System.Reflection.Emit => 95
	i64 u0xd4146544adb48430, ; 911: lib_Masa.Blazor.MobileComponents.dll.so => 184
	i64 u0xd42655883bb8c19f, ; 912: Microsoft.EntityFrameworkCore.Abstractions.dll => 195
	i64 u0xd4645626dffec99d, ; 913: lib_Microsoft.Extensions.DependencyInjection.Abstractions.dll.so => 206
	i64 u0xd46b4a8758d1f3ee, ; 914: Microsoft.Extensions.FileProviders.Composite.dll => 211
	i64 u0xd4fa0abb79079ea9, ; 915: System.Security.Principal.dll => 131
	i64 u0xd504f952402ba83f, ; 916: OneOf.dll => 231
	i64 u0xd5507e11a2b2839f, ; 917: Xamarin.AndroidX.Lifecycle.ViewModelSavedState => 286
	i64 u0xd5d04bef8478ea19, ; 918: Xamarin.AndroidX.Tracing.Tracing.dll => 305
	i64 u0xd60815f26a12e140, ; 919: Microsoft.Extensions.Logging.Debug.dll => 218
	i64 u0xd6694f8359737e4e, ; 920: Xamarin.AndroidX.SavedState => 298
	i64 u0xd6949e129339eae5, ; 921: lib_Xamarin.AndroidX.Core.Core.Ktx.dll.so => 260
	i64 u0xd6d21782156bc35b, ; 922: Xamarin.AndroidX.SwipeRefreshLayout.dll => 304
	i64 u0xd6de019f6af72435, ; 923: Xamarin.AndroidX.ConstraintLayout.Core.dll => 257
	i64 u0xd70956d1e6deefb9, ; 924: Jsr305Binding => 317
	i64 u0xd72329819cbbbc44, ; 925: lib_Microsoft.Extensions.Configuration.Abstractions.dll.so => 201
	i64 u0xd72c760af136e863, ; 926: System.Xml.XmlSerializer.dll => 166
	i64 u0xd753f071e44c2a03, ; 927: lib_System.Security.SecureString.dll.so => 132
	i64 u0xd7b3764ada9d341d, ; 928: lib_Microsoft.Extensions.Logging.Abstractions.dll.so => 217
	i64 u0xd7f0088bc5ad71f2, ; 929: Xamarin.AndroidX.VersionedParcelable => 310
	i64 u0xd824ef6ab33f8f7a, ; 930: Xamarin.AndroidX.Window.WindowCore.dll => 314
	i64 u0xd8fb25e28ae30a12, ; 931: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll => 295
	i64 u0xda1dfa4c534a9251, ; 932: Microsoft.Extensions.DependencyInjection => 205
	i64 u0xdad05a11827959a3, ; 933: System.Collections.NonGeneric.dll => 10
	i64 u0xdaec201e20d8c9b8, ; 934: lib_Masa.Blazor.dll.so => 183
	i64 u0xdaefdfe71aa53cf9, ; 935: System.IO.FileSystem.Primitives => 49
	i64 u0xdb1a41d72ea6dd00, ; 936: Masa.Blazor.MobileComponents => 184
	i64 u0xdb5383ab5865c007, ; 937: lib-vi-Microsoft.Maui.Controls.resources.dll.so => 359
	i64 u0xdb58816721c02a59, ; 938: lib_System.Reflection.Emit.ILGeneration.dll.so => 93
	i64 u0xdbeda89f832aa805, ; 939: vi/Microsoft.Maui.Controls.resources.dll => 359
	i64 u0xdbf2a779fbc3ac31, ; 940: System.Transactions.Local.dll => 153
	i64 u0xdbf9607a441b4505, ; 941: System.Linq => 63
	i64 u0xdbfc90157a0de9b0, ; 942: lib_System.Text.Encoding.dll.so => 138
	i64 u0xdc75032002d1a212, ; 943: lib_System.Transactions.Local.dll.so => 153
	i64 u0xdca8be7403f92d4f, ; 944: lib_System.Linq.Queryable.dll.so => 62
	i64 u0xdce2c53525640bf3, ; 945: Microsoft.Extensions.Logging => 216
	i64 u0xdce6603fe3f1713a, ; 946: FinanceTracker.Web => 366
	i64 u0xdd2b722d78ef5f43, ; 947: System.Runtime.dll => 119
	i64 u0xdd67031857c72f96, ; 948: lib_System.Text.Encodings.Web.dll.so => 139
	i64 u0xdd70765ad6162057, ; 949: Xamarin.JSpecify => 322
	i64 u0xdd92e229ad292030, ; 950: System.Numerics.dll => 86
	i64 u0xdde30e6b77aa6f6c, ; 951: lib-zh-Hans-Microsoft.Maui.Controls.resources.dll.so => 361
	i64 u0xde110ae80fa7c2e2, ; 952: System.Xml.XDocument.dll => 162
	i64 u0xde4726fcdf63a198, ; 953: Xamarin.AndroidX.Transition => 307
	i64 u0xde572c2b2fb32f93, ; 954: lib_System.Threading.Tasks.Extensions.dll.so => 146
	i64 u0xde8769ebda7d8647, ; 955: hr/Microsoft.Maui.Controls.resources.dll => 340
	i64 u0xdee075f3477ef6be, ; 956: Xamarin.AndroidX.ExifInterface.dll => 269
	i64 u0xdf4b773de8fb1540, ; 957: System.Net.dll => 84
	i64 u0xdf9c7682560a9629, ; 958: System.Net.ServerSentEvents => 76
	i64 u0xdfa254ebb4346068, ; 959: System.Net.Ping => 71
	i64 u0xe0142572c095a480, ; 960: Xamarin.AndroidX.AppCompat.dll => 246
	i64 u0xe021eaa401792a05, ; 961: System.Text.Encoding.dll => 138
	i64 u0xe02f89350ec78051, ; 962: Xamarin.AndroidX.CoordinatorLayout.dll => 258
	i64 u0xe0496b9d65ef5474, ; 963: Xamarin.Android.Glide.DiskLruCache.dll => 239
	i64 u0xe10b760bb1462e7a, ; 964: lib_System.Security.Cryptography.Primitives.dll.so => 127
	i64 u0xe192a588d4410686, ; 965: lib_System.IO.Pipelines.dll.so => 54
	i64 u0xe1a08bd3fa539e0d, ; 966: System.Runtime.Loader => 112
	i64 u0xe1a77eb8831f7741, ; 967: System.Security.SecureString.dll => 132
	i64 u0xe1b52f9f816c70ef, ; 968: System.Private.Xml.Linq.dll => 90
	i64 u0xe1e199c8ab02e356, ; 969: System.Data.DataSetExtensions.dll => 23
	i64 u0xe1ecfdb7fff86067, ; 970: System.Net.Security.dll => 75
	i64 u0xe2252a80fe853de4, ; 971: lib_System.Security.Principal.dll.so => 131
	i64 u0xe22fa4c9c645db62, ; 972: System.Diagnostics.TextWriterTraceListener.dll => 31
	i64 u0xe24095a7afddaab3, ; 973: lib_Microsoft.Extensions.Hosting.Abstractions.dll.so => 215
	i64 u0xe2420585aeceb728, ; 974: System.Net.Requests.dll => 74
	i64 u0xe26692647e6bcb62, ; 975: Xamarin.AndroidX.Lifecycle.Runtime.Ktx => 281
	i64 u0xe29b73bc11392966, ; 976: lib-id-Microsoft.Maui.Controls.resources.dll.so => 342
	i64 u0xe2ad448dee50fbdf, ; 977: System.Xml.Serialization => 161
	i64 u0xe2d920f978f5d85c, ; 978: System.Data.DataSetExtensions => 23
	i64 u0xe2e426c7714fa0bc, ; 979: Microsoft.Win32.Primitives.dll => 4
	i64 u0xe31089e70e4e84ee, ; 980: Microsoft.AspNetCore.Components.WebView.Maui => 191
	i64 u0xe332bacb3eb4a806, ; 981: Mono.Android.Export.dll => 173
	i64 u0xe37a8b70fdfbeb66, ; 982: Util.Reflection => 236
	i64 u0xe3811d68d4fe8463, ; 983: pt-BR/Microsoft.Maui.Controls.resources.dll => 350
	i64 u0xe3b7cbae5ad66c75, ; 984: lib_System.Security.Cryptography.Encoding.dll.so => 125
	i64 u0xe4292b48f3224d5b, ; 985: lib_Xamarin.AndroidX.Core.ViewTree.dll.so => 261
	i64 u0xe494f7ced4ecd10a, ; 986: hu/Microsoft.Maui.Controls.resources.dll => 341
	i64 u0xe4a9b1e40d1e8917, ; 987: lib-fi-Microsoft.Maui.Controls.resources.dll.so => 336
	i64 u0xe4f74a0b5bf9703f, ; 988: System.Runtime.Serialization.Primitives => 116
	i64 u0xe5434e8a119ceb69, ; 989: lib_Mono.Android.dll.so => 175
	i64 u0xe55703b9ce5c038a, ; 990: System.Diagnostics.Tools => 32
	i64 u0xe57013c8afc270b5, ; 991: Microsoft.VisualBasic => 3
	i64 u0xe62913cc36bc07ec, ; 992: System.Xml.dll => 167
	i64 u0xe7bea09c4900a191, ; 993: Xamarin.AndroidX.VectorDrawable.dll => 308
	i64 u0xe7e03cc18dcdeb49, ; 994: lib_System.Diagnostics.StackTrace.dll.so => 30
	i64 u0xe7e147ff99a7a380, ; 995: lib_System.Configuration.dll.so => 19
	i64 u0xe8397cf3948e7cb7, ; 996: lib_Microsoft.Extensions.Options.ConfigurationExtensions.dll.so => 220
	i64 u0xe86b0df4ba9e5db8, ; 997: lib_Xamarin.AndroidX.Lifecycle.Runtime.Android.dll.so => 280
	i64 u0xe896622fe0902957, ; 998: System.Reflection.Emit.dll => 95
	i64 u0xe89a2a9ef110899b, ; 999: System.Drawing.dll => 36
	i64 u0xe8c5f8c100b5934b, ; 1000: Microsoft.Win32.Registry => 5
	i64 u0xe9772100456fb4b4, ; 1001: Microsoft.AspNetCore.Components.dll => 186
	i64 u0xe98163eb702ae5c5, ; 1002: Xamarin.AndroidX.Arch.Core.Runtime => 249
	i64 u0xe994f23ba4c143e5, ; 1003: Xamarin.KotlinX.Coroutines.Android => 324
	i64 u0xe9b9c8c0458fd92a, ; 1004: System.Windows => 158
	i64 u0xe9d166d87a7f2bdb, ; 1005: lib_Xamarin.AndroidX.Startup.StartupRuntime.dll.so => 303
	i64 u0xea154e342c6ac70f, ; 1006: Microsoft.Extensions.FileProviders.Embedded.dll => 212
	i64 u0xea5a4efc2ad81d1b, ; 1007: Xamarin.Google.ErrorProne.Annotations => 319
	i64 u0xeacb43ab5f77309d, ; 1008: FinanceTracker.Infrastructure => 364
	i64 u0xeb2313fe9d65b785, ; 1009: Xamarin.AndroidX.ConstraintLayout.dll => 256
	i64 u0xec8abb68d340aac6, ; 1010: Microsoft.AspNetCore.Authorization => 185
	i64 u0xed19c616b3fcb7eb, ; 1011: Xamarin.AndroidX.VersionedParcelable.dll => 310
	i64 u0xedc4817167106c23, ; 1012: System.Net.Sockets.dll => 78
	i64 u0xedc632067fb20ff3, ; 1013: System.Memory.dll => 64
	i64 u0xedc8e4ca71a02a8b, ; 1014: Xamarin.AndroidX.Navigation.Runtime.dll => 292
	i64 u0xee81f5b3f1c4f83b, ; 1015: System.Threading.ThreadPool => 150
	i64 u0xeeb7ebb80150501b, ; 1016: lib_Xamarin.AndroidX.Collection.Jvm.dll.so => 253
	i64 u0xeefc635595ef57f0, ; 1017: System.Security.Cryptography.Cng => 123
	i64 u0xef03b1b5a04e9709, ; 1018: System.Text.Encoding.CodePages.dll => 136
	i64 u0xef5bcbe61622ee5f, ; 1019: Xamarin.AndroidX.Tracing.Tracing.Android.dll => 306
	i64 u0xef602c523fe2e87a, ; 1020: lib_Xamarin.Google.Guava.ListenableFuture.dll.so => 320
	i64 u0xef72742e1bcca27a, ; 1021: Microsoft.Maui.Essentials.dll => 227
	i64 u0xefd1e0c4e5c9b371, ; 1022: System.Resources.ResourceManager.dll => 102
	i64 u0xefe8f8d5ed3c72ea, ; 1023: System.Formats.Tar.dll => 39
	i64 u0xefec0b7fdc57ec42, ; 1024: Xamarin.AndroidX.Activity => 241
	i64 u0xeff59cbde4363ec3, ; 1025: System.Threading.AccessControl.dll => 142
	i64 u0xf00c29406ea45e19, ; 1026: es/Microsoft.Maui.Controls.resources.dll => 335
	i64 u0xf09e47b6ae914f6e, ; 1027: System.Net.NameResolution => 69
	i64 u0xf0ac2b489fed2e35, ; 1028: lib_System.Diagnostics.Debug.dll.so => 26
	i64 u0xf0bb49dadd3a1fe1, ; 1029: lib_System.Net.ServicePoint.dll.so => 77
	i64 u0xf0c16dff90fbf5d6, ; 1030: Xamarin.AndroidX.Window.WindowCore.Jvm => 315
	i64 u0xf0de2537ee19c6ca, ; 1031: lib_System.Net.WebHeaderCollection.dll.so => 80
	i64 u0xf1138779fa181c68, ; 1032: lib_Xamarin.AndroidX.Lifecycle.Runtime.dll.so => 279
	i64 u0xf11b621fc87b983f, ; 1033: Microsoft.Maui.Controls.Xaml.dll => 225
	i64 u0xf137a70c256dede6, ; 1034: FluentValidation.DependencyInjectionExtensions => 181
	i64 u0xf161f4f3c3b7e62c, ; 1035: System.Data => 24
	i64 u0xf16eb650d5a464bc, ; 1036: System.ValueTuple => 155
	i64 u0xf1c4b4005493d871, ; 1037: System.Formats.Asn1.dll => 38
	i64 u0xf22514cfad2d598b, ; 1038: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll.so => 287
	i64 u0xf238bd79489d3a96, ; 1039: lib-nl-Microsoft.Maui.Controls.resources.dll.so => 348
	i64 u0xf2feea356ba760af, ; 1040: Xamarin.AndroidX.Arch.Core.Runtime.dll => 249
	i64 u0xf300e085f8acd238, ; 1041: lib_System.ServiceProcess.dll.so => 135
	i64 u0xf34e52b26e7e059d, ; 1042: System.Runtime.CompilerServices.VisualC.dll => 105
	i64 u0xf37221fda4ef8830, ; 1043: lib_Xamarin.Google.Android.Material.dll.so => 316
	i64 u0xf3ad9b8fb3eefd12, ; 1044: lib_System.IO.UnmanagedMemoryStream.dll.so => 57
	i64 u0xf3ddfe05336abf29, ; 1045: System => 168
	i64 u0xf408654b2a135055, ; 1046: System.Reflection.Emit.ILGeneration.dll => 93
	i64 u0xf4103170a1de5bd0, ; 1047: System.Linq.Queryable.dll => 62
	i64 u0xf42d20c23173d77c, ; 1048: lib_System.ServiceModel.Web.dll.so => 134
	i64 u0xf4c1dd70a5496a17, ; 1049: System.IO.Compression => 46
	i64 u0xf4ecf4b9afc64781, ; 1050: System.ServiceProcess.dll => 135
	i64 u0xf4eeeaa566e9b970, ; 1051: lib_Xamarin.AndroidX.CustomView.PoolingContainer.dll.so => 264
	i64 u0xf518f63ead11fcd1, ; 1052: System.Threading.Tasks => 148
	i64 u0xf5fc7602fe27b333, ; 1053: System.Net.WebHeaderCollection => 80
	i64 u0xf6077741019d7428, ; 1054: Xamarin.AndroidX.CoordinatorLayout => 258
	i64 u0xf6742cbf457c450b, ; 1055: Xamarin.AndroidX.Lifecycle.Runtime.Android.dll => 280
	i64 u0xf6de7fa3776f8927, ; 1056: lib_Microsoft.Extensions.Configuration.Json.dll.so => 204
	i64 u0xf6e8de2aebcbb422, ; 1057: lib_Xamarin.AndroidX.Window.WindowCore.Jvm.dll.so => 315
	i64 u0xf6f893f692f8cb43, ; 1058: Microsoft.Extensions.Options.ConfigurationExtensions.dll => 220
	i64 u0xf70c0a7bf8ccf5af, ; 1059: System.Web => 157
	i64 u0xf77b20923f07c667, ; 1060: de/Microsoft.Maui.Controls.resources.dll => 333
	i64 u0xf79cbf52994c8548, ; 1061: Npgsql => 229
	i64 u0xf7e2cac4c45067b3, ; 1062: lib_System.Numerics.Vectors.dll.so => 85
	i64 u0xf7e74930e0e3d214, ; 1063: zh-HK/Microsoft.Maui.Controls.resources.dll => 360
	i64 u0xf84773b5c81e3cef, ; 1064: lib-uk-Microsoft.Maui.Controls.resources.dll.so => 358
	i64 u0xf8aac5ea82de1348, ; 1065: System.Linq.Queryable => 62
	i64 u0xf8b77539b362d3ba, ; 1066: lib_System.Reflection.Primitives.dll.so => 98
	i64 u0xf8e045dc345b2ea3, ; 1067: lib_Xamarin.AndroidX.RecyclerView.dll.so => 296
	i64 u0xf915dc29808193a1, ; 1068: System.Web.HttpUtility.dll => 156
	i64 u0xf96c777a2a0686f4, ; 1069: hi/Microsoft.Maui.Controls.resources.dll => 339
	i64 u0xf9be54c8bcf8ff3b, ; 1070: System.Security.AccessControl.dll => 120
	i64 u0xf9eec5bb3a6aedc6, ; 1071: Microsoft.Extensions.Options => 219
	i64 u0xfa0e82300e67f913, ; 1072: lib_System.AppContext.dll.so => 6
	i64 u0xfa2fdb27e8a2c8e8, ; 1073: System.ComponentModel.EventBasedAsync => 15
	i64 u0xfa3f278f288b0e84, ; 1074: lib_System.Net.Security.dll.so => 75
	i64 u0xfa504dfa0f097d72, ; 1075: Microsoft.Extensions.FileProviders.Abstractions.dll => 210
	i64 u0xfa5ed7226d978949, ; 1076: lib-ar-Microsoft.Maui.Controls.resources.dll.so => 329
	i64 u0xfa645d91e9fc4cba, ; 1077: System.Threading.Thread => 149
	i64 u0xfad4d2c770e827f9, ; 1078: lib_System.IO.IsolatedStorage.dll.so => 52
	i64 u0xfb022853d73b7fa5, ; 1079: lib_SQLitePCLRaw.batteries_v2.dll.so => 232
	i64 u0xfb06dd2338e6f7c4, ; 1080: System.Net.Ping.dll => 71
	i64 u0xfb087abe5365e3b7, ; 1081: lib_System.Data.DataSetExtensions.dll.so => 23
	i64 u0xfb846e949baff5ea, ; 1082: System.Xml.Serialization.dll => 161
	i64 u0xfbad3e4ce4b98145, ; 1083: System.Security.Cryptography.X509Certificates => 128
	i64 u0xfbf0a31c9fc34bc4, ; 1084: lib_System.Net.Http.dll.so => 66
	i64 u0xfc6b7527cc280b3f, ; 1085: lib_System.Runtime.Serialization.Formatters.dll.so => 114
	i64 u0xfc719aec26adf9d9, ; 1086: Xamarin.AndroidX.Navigation.Fragment.dll => 291
	i64 u0xfc82690c2fe2735c, ; 1087: Xamarin.AndroidX.Lifecycle.Process.dll => 278
	i64 u0xfc93fc307d279893, ; 1088: System.IO.Pipes.AccessControl.dll => 55
	i64 u0xfcd302092ada6328, ; 1089: System.IO.MemoryMappedFiles.dll => 53
	i64 u0xfd22f00870e40ae0, ; 1090: lib_Xamarin.AndroidX.DrawerLayout.dll.so => 265
	i64 u0xfd2e866c678cac90, ; 1091: lib_Microsoft.AspNetCore.Components.WebView.Maui.dll.so => 191
	i64 u0xfd49b3c1a76e2748, ; 1092: System.Runtime.InteropServices.RuntimeInformation => 109
	i64 u0xfd536c702f64dc47, ; 1093: System.Text.Encoding.Extensions => 137
	i64 u0xfd583f7657b6a1cb, ; 1094: Xamarin.AndroidX.Fragment => 270
	i64 u0xfd8dd91a2c26bd5d, ; 1095: Xamarin.AndroidX.Lifecycle.Runtime => 279
	i64 u0xfda36abccf05cf5c, ; 1096: System.Net.WebSockets.Client => 82
	i64 u0xfddbe9695626a7f5, ; 1097: Xamarin.AndroidX.Lifecycle.Common => 273
	i64 u0xfe9856c3af9365ab, ; 1098: lib_Microsoft.Extensions.Configuration.FileExtensions.dll.so => 203
	i64 u0xfeae9952cf03b8cb, ; 1099: tr/Microsoft.Maui.Controls.resources => 357
	i64 u0xfebe1950717515f9, ; 1100: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll => 277
	i64 u0xff270a55858bac8d, ; 1101: System.Security.Principal => 131
	i64 u0xff9b54613e0d2cc8, ; 1102: System.Net.Http.Json => 65
	i64 u0xffdb7a971be4ec73 ; 1103: System.ValueTuple.dll => 155
], align 8

@assembly_image_cache_indices = dso_local local_unnamed_addr constant [1104 x i32] [
	i32 42, i32 325, i32 304, i32 13, i32 292, i32 220, i32 107, i32 199,
	i32 174, i32 48, i32 246, i32 7, i32 235, i32 88, i32 353, i32 331,
	i32 359, i32 266, i32 72, i32 296, i32 193, i32 12, i32 226, i32 104,
	i32 193, i32 360, i32 159, i32 19, i32 271, i32 253, i32 186, i32 164,
	i32 268, i32 308, i32 170, i32 181, i32 353, i32 10, i32 218, i32 309,
	i32 98, i32 264, i32 265, i32 13, i32 219, i32 10, i32 129, i32 97,
	i32 179, i32 198, i32 143, i32 39, i32 354, i32 328, i32 311, i32 350,
	i32 175, i32 240, i32 5, i32 227, i32 68, i32 301, i32 132, i32 300,
	i32 267, i32 69, i32 0, i32 254, i32 67, i32 57, i32 263, i32 52,
	i32 43, i32 127, i32 68, i32 83, i32 281, i32 161, i32 94, i32 101,
	i32 296, i32 144, i32 154, i32 250, i32 337, i32 165, i32 172, i32 338,
	i32 206, i32 83, i32 322, i32 254, i32 4, i32 5, i32 51, i32 103,
	i32 207, i32 56, i32 122, i32 100, i32 171, i32 120, i32 325, i32 21,
	i32 230, i32 341, i32 139, i32 99, i32 328, i32 79, i32 347, i32 303,
	i32 121, i32 8, i32 168, i32 356, i32 71, i32 239, i32 188, i32 282,
	i32 297, i32 208, i32 213, i32 174, i32 148, i32 40, i32 301, i32 47,
	i32 30, i32 294, i32 345, i32 147, i32 219, i32 166, i32 28, i32 86,
	i32 305, i32 79, i32 43, i32 29, i32 222, i32 42, i32 105, i32 119,
	i32 244, i32 45, i32 93, i32 356, i32 56, i32 151, i32 149, i32 194,
	i32 102, i32 49, i32 20, i32 259, i32 116, i32 237, i32 337, i32 318,
	i32 232, i32 323, i32 221, i32 96, i32 58, i32 342, i32 340, i32 222,
	i32 314, i32 83, i32 318, i32 172, i32 26, i32 72, i32 236, i32 295,
	i32 209, i32 269, i32 290, i32 358, i32 70, i32 33, i32 336, i32 14,
	i32 141, i32 38, i32 362, i32 214, i32 255, i32 349, i32 136, i32 94,
	i32 90, i32 152, i32 315, i32 355, i32 24, i32 140, i32 57, i32 142,
	i32 51, i32 334, i32 223, i32 29, i32 160, i32 34, i32 167, i32 198,
	i32 180, i32 270, i32 52, i32 212, i32 367, i32 313, i32 92, i32 251,
	i32 35, i32 337, i32 160, i32 214, i32 9, i32 335, i32 78, i32 59,
	i32 55, i32 211, i32 226, i32 331, i32 224, i32 13, i32 312, i32 200,
	i32 248, i32 111, i32 285, i32 229, i32 32, i32 106, i32 86, i32 94,
	i32 53, i32 98, i32 321, i32 58, i32 9, i32 104, i32 263, i32 69,
	i32 311, i32 330, i32 210, i32 127, i32 297, i32 118, i32 137, i32 223,
	i32 299, i32 128, i32 108, i32 323, i32 133, i32 250, i32 320, i32 150,
	i32 159, i32 271, i32 259, i32 232, i32 203, i32 266, i32 297, i32 99,
	i32 24, i32 190, i32 302, i32 146, i32 215, i32 289, i32 3, i32 170,
	i32 247, i32 102, i32 164, i32 101, i32 261, i32 25, i32 95, i32 171,
	i32 175, i32 242, i32 3, i32 349, i32 306, i32 268, i32 187, i32 1,
	i32 178, i32 116, i32 323, i32 195, i32 271, i32 278, i32 33, i32 6,
	i32 207, i32 353, i32 159, i32 236, i32 351, i32 53, i32 87, i32 310,
	i32 294, i32 44, i32 277, i32 106, i32 47, i32 140, i32 287, i32 65,
	i32 196, i32 288, i32 70, i32 82, i32 60, i32 179, i32 91, i32 157,
	i32 248, i32 135, i32 112, i32 59, i32 343, i32 288, i32 223, i32 295,
	i32 174, i32 136, i32 143, i32 40, i32 330, i32 234, i32 202, i32 224,
	i32 61, i32 202, i32 284, i32 81, i32 25, i32 36, i32 101, i32 281,
	i32 72, i32 22, i32 259, i32 228, i32 354, i32 123, i32 70, i32 109,
	i32 360, i32 121, i32 119, i32 273, i32 290, i32 274, i32 11, i32 2,
	i32 126, i32 117, i32 145, i32 41, i32 89, i32 243, i32 233, i32 177,
	i32 215, i32 27, i32 151, i32 202, i32 344, i32 205, i32 319, i32 242,
	i32 1, i32 244, i32 44, i32 258, i32 152, i32 18, i32 88, i32 332,
	i32 41, i32 277, i32 252, i32 282, i32 96, i32 216, i32 28, i32 41,
	i32 80, i32 191, i32 267, i32 255, i32 363, i32 147, i32 110, i32 253,
	i32 11, i32 107, i32 139, i32 16, i32 124, i32 67, i32 178, i32 160,
	i32 22, i32 234, i32 334, i32 327, i32 104, i32 205, i32 326, i32 64,
	i32 183, i32 58, i32 225, i32 333, i32 112, i32 177, i32 293, i32 324,
	i32 9, i32 316, i32 122, i32 100, i32 107, i32 76, i32 285, i32 224,
	i32 113, i32 245, i32 49, i32 59, i32 20, i32 284, i32 262, i32 73,
	i32 257, i32 187, i32 158, i32 39, i32 363, i32 332, i32 192, i32 35,
	i32 38, i32 338, i32 234, i32 110, i32 347, i32 21, i32 321, i32 366,
	i32 283, i32 228, i32 15, i32 221, i32 81, i32 81, i32 262, i32 221,
	i32 291, i32 300, i32 155, i32 21, i32 226, i32 331, i32 50, i32 51,
	i32 222, i32 357, i32 347, i32 96, i32 238, i32 209, i32 343, i32 16,
	i32 261, i32 125, i32 179, i32 340, i32 163, i32 366, i32 45, i32 319,
	i32 182, i32 118, i32 64, i32 197, i32 169, i32 208, i32 200, i32 14,
	i32 298, i32 113, i32 245, i32 61, i32 204, i32 76, i32 123, i32 346,
	i32 2, i32 356, i32 270, i32 283, i32 322, i32 190, i32 283, i32 6,
	i32 252, i32 336, i32 266, i32 193, i32 17, i32 354, i32 333, i32 79,
	i32 256, i32 192, i32 290, i32 133, i32 321, i32 346, i32 188, i32 85,
	i32 218, i32 12, i32 34, i32 121, i32 327, i32 278, i32 176, i32 268,
	i32 87, i32 237, i32 18, i32 311, i32 201, i32 276, i32 73, i32 97,
	i32 235, i32 189, i32 168, i32 272, i32 84, i32 362, i32 246, i32 251,
	i32 157, i32 36, i32 154, i32 358, i32 361, i32 211, i32 147, i32 56,
	i32 115, i32 196, i32 252, i32 308, i32 307, i32 365, i32 0, i32 37,
	i32 362, i32 200, i32 117, i32 214, i32 244, i32 14, i32 238, i32 195,
	i32 149, i32 43, i32 227, i32 242, i32 100, i32 326, i32 171, i32 16,
	i32 299, i32 48, i32 109, i32 99, i32 198, i32 288, i32 27, i32 130,
	i32 29, i32 338, i32 210, i32 300, i32 130, i32 44, i32 262, i32 197,
	i32 267, i32 152, i32 8, i32 306, i32 289, i32 339, i32 352, i32 233,
	i32 351, i32 134, i32 350, i32 42, i32 327, i32 233, i32 33, i32 367,
	i32 46, i32 146, i32 284, i32 225, i32 275, i32 263, i32 204, i32 140,
	i32 63, i32 134, i32 330, i32 48, i32 163, i32 249, i32 275, i32 238,
	i32 273, i32 346, i32 307, i32 46, i32 167, i32 176, i32 272, i32 335,
	i32 269, i32 342, i32 228, i32 18, i32 8, i32 182, i32 260, i32 299,
	i32 126, i32 60, i32 144, i32 292, i32 345, i32 279, i32 317, i32 229,
	i32 313, i32 153, i32 145, i32 325, i32 128, i32 324, i32 163, i32 165,
	i32 264, i32 241, i32 201, i32 293, i32 348, i32 26, i32 231, i32 187,
	i32 289, i32 276, i32 213, i32 84, i32 313, i32 129, i32 318, i32 103,
	i32 151, i32 180, i32 316, i32 314, i32 294, i32 230, i32 54, i32 165,
	i32 364, i32 170, i32 184, i32 133, i32 212, i32 37, i32 309, i32 345,
	i32 208, i32 188, i32 22, i32 114, i32 92, i32 50, i32 61, i32 124,
	i32 85, i32 129, i32 166, i32 317, i32 169, i32 298, i32 301, i32 265,
	i32 237, i32 280, i32 4, i32 274, i32 341, i32 173, i32 2, i32 213,
	i32 285, i32 363, i32 118, i32 243, i32 19, i32 217, i32 91, i32 66,
	i32 30, i32 206, i32 334, i32 257, i32 60, i32 113, i32 276, i32 32,
	i32 130, i32 162, i32 352, i32 255, i32 143, i32 348, i32 156, i32 17,
	i32 254, i32 240, i32 77, i32 75, i32 15, i32 172, i32 87, i32 197,
	i32 126, i32 275, i32 286, i32 256, i32 355, i32 0, i32 282, i32 34,
	i32 186, i32 120, i32 141, i32 365, i32 124, i32 108, i32 332, i32 309,
	i32 231, i32 251, i32 339, i32 329, i32 54, i32 47, i32 28, i32 142,
	i32 148, i32 217, i32 150, i32 35, i32 355, i32 177, i32 77, i32 164,
	i32 1, i32 287, i32 302, i32 351, i32 344, i32 162, i32 12, i32 158,
	i32 190, i32 154, i32 178, i32 78, i32 105, i32 183, i32 114, i32 235,
	i32 248, i32 192, i32 185, i32 66, i32 67, i32 312, i32 45, i32 250,
	i32 111, i32 7, i32 247, i32 55, i32 243, i32 365, i32 65, i32 329,
	i32 260, i32 20, i32 111, i32 103, i32 63, i32 145, i32 194, i32 185,
	i32 241, i32 181, i32 7, i32 344, i32 173, i32 50, i32 312, i32 117,
	i32 207, i32 144, i32 182, i32 169, i32 82, i32 364, i32 115, i32 194,
	i32 286, i32 199, i32 17, i32 74, i32 291, i32 91, i32 239, i32 89,
	i32 122, i32 305, i32 203, i32 245, i32 293, i32 137, i32 156, i32 108,
	i32 11, i32 92, i32 31, i32 189, i32 199, i32 357, i32 138, i32 349,
	i32 352, i32 303, i32 196, i32 230, i32 240, i32 40, i32 367, i32 302,
	i32 141, i32 326, i32 328, i32 25, i32 176, i32 361, i32 74, i32 272,
	i32 304, i32 27, i32 68, i32 90, i32 180, i32 97, i32 115, i32 31,
	i32 106, i32 274, i32 37, i32 209, i32 73, i32 189, i32 320, i32 110,
	i32 125, i32 247, i32 89, i32 216, i32 88, i32 343, i32 95, i32 184,
	i32 195, i32 206, i32 211, i32 131, i32 231, i32 286, i32 305, i32 218,
	i32 298, i32 260, i32 304, i32 257, i32 317, i32 201, i32 166, i32 132,
	i32 217, i32 310, i32 314, i32 295, i32 205, i32 10, i32 183, i32 49,
	i32 184, i32 359, i32 93, i32 359, i32 153, i32 63, i32 138, i32 153,
	i32 62, i32 216, i32 366, i32 119, i32 139, i32 322, i32 86, i32 361,
	i32 162, i32 307, i32 146, i32 340, i32 269, i32 84, i32 76, i32 71,
	i32 246, i32 138, i32 258, i32 239, i32 127, i32 54, i32 112, i32 132,
	i32 90, i32 23, i32 75, i32 131, i32 31, i32 215, i32 74, i32 281,
	i32 342, i32 161, i32 23, i32 4, i32 191, i32 173, i32 236, i32 350,
	i32 125, i32 261, i32 341, i32 336, i32 116, i32 175, i32 32, i32 3,
	i32 167, i32 308, i32 30, i32 19, i32 220, i32 280, i32 95, i32 36,
	i32 5, i32 186, i32 249, i32 324, i32 158, i32 303, i32 212, i32 319,
	i32 364, i32 256, i32 185, i32 310, i32 78, i32 64, i32 292, i32 150,
	i32 253, i32 123, i32 136, i32 306, i32 320, i32 227, i32 102, i32 39,
	i32 241, i32 142, i32 335, i32 69, i32 26, i32 77, i32 315, i32 80,
	i32 279, i32 225, i32 181, i32 24, i32 155, i32 38, i32 287, i32 348,
	i32 249, i32 135, i32 105, i32 316, i32 57, i32 168, i32 93, i32 62,
	i32 134, i32 46, i32 135, i32 264, i32 148, i32 80, i32 258, i32 280,
	i32 204, i32 315, i32 220, i32 157, i32 333, i32 229, i32 85, i32 360,
	i32 358, i32 62, i32 98, i32 296, i32 156, i32 339, i32 120, i32 219,
	i32 6, i32 15, i32 75, i32 210, i32 329, i32 149, i32 52, i32 232,
	i32 71, i32 23, i32 161, i32 128, i32 66, i32 114, i32 291, i32 278,
	i32 55, i32 53, i32 265, i32 191, i32 109, i32 137, i32 270, i32 279,
	i32 82, i32 273, i32 203, i32 357, i32 277, i32 131, i32 65, i32 155
], align 4

@marshal_methods_number_of_classes = dso_local local_unnamed_addr constant i32 0, align 4

@marshal_methods_class_cache = dso_local local_unnamed_addr global [0 x %struct.MarshalMethodsManagedClass] zeroinitializer, align 8

; Names of classes in which marshal methods reside
@mm_class_names = dso_local local_unnamed_addr constant [0 x ptr] zeroinitializer, align 8

@mm_method_names = dso_local local_unnamed_addr constant [1 x %struct.MarshalMethodName] [
	%struct.MarshalMethodName {
		i64 u0x0000000000000000, ; name: 
		ptr @.MarshalMethodName.0_name; char* name
	} ; 0
], align 8

; get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr)
@get_function_pointer = internal dso_local unnamed_addr global ptr null, align 8

; Functions

; Function attributes: memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" uwtable willreturn
define void @xamarin_app_init(ptr nocapture noundef readnone %env, ptr noundef %fn) local_unnamed_addr #0
{
	%fnIsNull = icmp eq ptr %fn, null
	br i1 %fnIsNull, label %1, label %2

1: ; preds = %0
	%putsResult = call noundef i32 @puts(ptr @.mm.0)
	call void @abort()
	unreachable 

2: ; preds = %1, %0
	store ptr %fn, ptr @get_function_pointer, align 8, !tbaa !3
	ret void
}

; Strings
@.mm.0 = private unnamed_addr constant [40 x i8] c"get_function_pointer MUST be specified\0A\00", align 1

;MarshalMethodName
@.MarshalMethodName.0_name = private unnamed_addr constant [1 x i8] c"\00", align 1

; External functions

; Function attributes: "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8"
declare void @abort() local_unnamed_addr #2

; Function attributes: nofree nounwind
declare noundef i32 @puts(ptr noundef) local_unnamed_addr #1
attributes #0 = { memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+fix-cortex-a53-835769,+neon,+outline-atomics,+v8a" uwtable willreturn }
attributes #1 = { nofree nounwind }
attributes #2 = { "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+fix-cortex-a53-835769,+neon,+outline-atomics,+v8a" }

; Metadata
!llvm.module.flags = !{!0, !1, !7, !8, !9, !10}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!".NET for Android remotes/origin/release/10.0.1xx @ d549e1dc4e2a083b08b4f24cb5495e81b99d79b5"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
!7 = !{i32 1, !"branch-target-enforcement", i32 0}
!8 = !{i32 1, !"sign-return-address", i32 0}
!9 = !{i32 1, !"sign-return-address-all", i32 0}
!10 = !{i32 1, !"sign-return-address-with-bkey", i32 0}
