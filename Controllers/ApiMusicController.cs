using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicPlayerWeb.Models;
using System;
using System.Linq;

namespace MusicPlayerWeb.Controllers
{
    // URL akses nanti: https://localhost:PORT/api/music/...
    [Route("api/music")]
    [ApiController]
    public class ApiMusicController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ApiMusicController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==================================================================
        // 1. GET: Ambil Daftar Lagu (Format JSON)
        // Bisa pilih mode: "Songs" (Default) atau "Discover" (Terbaru)
        // ==================================================================
        [HttpGet("list")]
        public IActionResult GetSongs([FromQuery] string mode = "Songs")
        {
            var query = _context.Songs
                .Include(s => s.Artist)
                .Include(s => s.Album)
                .AsQueryable();

            // Logika Filter (Sama seperti di MusicController tapi versi simpel)
            switch (mode.ToLower())
            {
                case "discover":
                    query = query.OrderByDescending(s => s.DateAdded);
                    break;
                case "liked":
                    query = query.Where(s => s.IsLiked);
                    break;
                case "songs":
                default:
                    query = query.OrderBy(s => s.Title);
                    break;
            }

            // PENTING: Kita pakai .Select() (Projection)
            // Agar tidak terjadi error "Circular Reference" dan JSON lebih rapi/ringan
            var result = query.Select(s => new
            {
                s.Id,
                s.Title,
                Artist = s.Artist.Name,
                Album = s.Album.Title,
                DurationFormatted = TimeSpan.FromSeconds(s.Duration).ToString(@"mm\:ss"),
                s.FilePath,
                s.IsLiked,
                s.DateAdded
            }).ToList();

            return Ok(result);
        }

        // ==================================================================
        // 2. POST: Tambah Lagu dari YouTube (Terima JSON Body)
        // ==================================================================
        [HttpPost("add-youtube")]
        public IActionResult AddYoutubeSong([FromBody] YoutubeImportDto req)
        {
            // Validasi Input
            if (req == null || string.IsNullOrEmpty(req.VideoId))
            {
                return BadRequest("Data tidak valid atau VideoId kosong.");
            }

            // 1. Cek Duplikasi
            string ytPath = "YT:" + req.VideoId;
            var existingSong = _context.Songs.FirstOrDefault(s => s.FilePath == ytPath);

            if (existingSong != null)
            {
                return Ok(new { message = "Lagu sudah ada.", id = existingSong.Id, isNew = false });
            }

            // 2. Handle Artist
            var artist = _context.Artists.FirstOrDefault(a => a.Name == req.Author);
            if (artist == null)
            {
                artist = new Artist { Name = req.Author };
                _context.Artists.Add(artist);
            }

            // 3. Handle Album
            var album = _context.Albums.FirstOrDefault(a => a.Title == "YouTube Imports" && a.Artist.Id == artist.Id);
            if (album == null)
            {
                album = new Album { Title = "YouTube Imports", Artist = artist, Year = DateTime.Now.Year };
                _context.Albums.Add(album);
            }

            // 4. Simpan Lagu
            var song = new Song
            {
                Title = req.Title,
                Duration = req.DurationSec,
                FilePath = ytPath,
                DateAdded = DateTime.Now,
                Artist = artist,
                Album = album,
                IsLiked = false
            };

            _context.Songs.Add(song);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetSongs), new { id = song.Id }, new { message = "Berhasil disimpan", id = song.Id, isNew = true });
        }

        // ==================================================================
        // 3. POST: Register Local File (File sudah ada di Disk Server)
        // Gunakan ini jika file sudah ada di folder D:\Lagu tapi belum masuk DB
        // ==================================================================
        [HttpPost("add-local-path")]
        public IActionResult AddLocalPath([FromBody] LocalPathDto req)
        {
            if (string.IsNullOrEmpty(req.FilePath)) return BadRequest("File Path tidak boleh kosong.");

            // 1. Cek apakah file fisik benar-benar ada di komputer server
            if (!System.IO.File.Exists(req.FilePath))
            {
                return NotFound($"File tidak ditemukan di path: {req.FilePath}");
            }

            // 2. Cek apakah sudah ada di Database
            if (_context.Songs.Any(s => s.FilePath == req.FilePath))
            {
                var existing = _context.Songs.First(s => s.FilePath == req.FilePath);
                return Ok(new { message = "Lagu sudah terdaftar.", id = existing.Id });
            }

            try
            {
                // 3. Baca Metadata menggunakan TagLib
                var tfile = TagLib.File.Create(req.FilePath);

                string title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(req.FilePath);
                string artistName = !string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer) ? tfile.Tag.FirstPerformer : "Unknown Artist";
                string albumTitle = !string.IsNullOrWhiteSpace(tfile.Tag.Album) ? tfile.Tag.Album : "Unknown Album";
                double duration = tfile.Properties.Duration.TotalSeconds;

                // 4. Simpan ke Database (Gunakan Helper Logic biar rapi)
                var song = SaveSongToDb(title, artistName, albumTitle, req.FilePath, duration);

                return Ok(new { message = "Berhasil didaftarkan!", id = song.Id, title = song.Title });
            }
            catch (Exception ex)
            {
                return BadRequest($"Gagal membaca file: {ex.Message}");
            }
        }

        // ==================================================================
        // 4. POST: Upload File (Kirim File Fisik dari Postman)
        // Gunakan ini jika ingin meng-copy file dari laptopmu ke server
        // ==================================================================
        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("File tidak valid.");

            // Validasi Ekstensi
            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".mp3" && ext != ".flac" && ext != ".m4a")
                return BadRequest("Hanya support mp3, flac, atau m4a.");

            // 1. Tentukan lokasi simpan (Misal: folder "Uploads" di dalam wwwroot atau folder musik)
            // Disini saya taruh di folder "C:\MyMusicPlayer_Uploads" (Sesuaikan path ini!)
            string uploadFolder = @"C:\MyMusicPlayer_Uploads";

            if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);

            // Buat nama file unik agar tidak bentrok
            string fileName = DateTime.Now.Ticks + "_" + file.FileName;
            string fullPath = Path.Combine(uploadFolder, fileName);

            // 2. Simpan File ke Disk
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 3. Baca Metadata & Simpan ke DB
            try
            {
                var tfile = TagLib.File.Create(fullPath);

                string title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(file.FileName);
                string artistName = !string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer) ? tfile.Tag.FirstPerformer : "Unknown Artist";
                string albumTitle = !string.IsNullOrWhiteSpace(tfile.Tag.Album) ? tfile.Tag.Album : "Uploads";
                double duration = tfile.Properties.Duration.TotalSeconds;

                var song = SaveSongToDb(title, artistName, albumTitle, fullPath, duration);

                return Ok(new { message = "File berhasil diupload dan disimpan!", id = song.Id, path = fullPath });
            }
            catch (Exception ex)
            {
                // Jika gagal baca tag, hapus file yang sudah terlanjur diupload (opsional)
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                return BadRequest($"Error membaca metadata: {ex.Message}");
            }
        }

        // --- HELPER FUNCTION (Private) ---
        // Agar kita tidak menulis ulang logika Artist/Album berulang kali
        private Song SaveSongToDb(string title, string artistName, string albumTitle, string filePath, double duration)
        {
            // Handle Artist
            var artist = _context.Artists.FirstOrDefault(a => a.Name == artistName);
            if (artist == null)
            {
                artist = new Artist { Name = artistName };
                _context.Artists.Add(artist);
            }

            // Handle Album
            var album = _context.Albums.FirstOrDefault(a => a.Title == albumTitle && a.Artist.Name == artistName);
            // Cek local tracker untuk menghindari duplikasi saat insert bulk
            if (album == null) album = _context.Albums.Local.FirstOrDefault(a => a.Title == albumTitle && a.Artist.Name == artistName);

            if (album == null)
            {
                album = new Album { Title = albumTitle, Artist = artist, Year = DateTime.Now.Year };
                _context.Albums.Add(album);
            }

            // Save Song
            var song = new Song
            {
                Title = title,
                Duration = duration,
                FilePath = filePath,
                DateAdded = DateTime.Now,
                Artist = artist,
                Album = album,
                IsLiked = false
            };

            _context.Songs.Add(song);
            _context.SaveChanges();
            return song;
        }

        // ==================================================================
        // 5. PUT: Edit Data Lagu (Update Metadata)
        // Menggunakan PUT karena kita mengupdate resource yang sudah ada
        // ==================================================================
        [HttpPut("edit")]
        public IActionResult EditSong([FromBody] UpdateSongDto req)
        {
            // 1. Cari Lagu di Database
            var song = _context.Songs
                .Include(s => s.Artist)
                .Include(s => s.Album)
                .FirstOrDefault(s => s.Id == req.Id);

            if (song == null)
            {
                return NotFound(new { message = $"Lagu dengan ID {req.Id} tidak ditemukan." });
            }

            // 2. Update Judul & IsLiked (Langsung)
            if (!string.IsNullOrEmpty(req.Title)) song.Title = req.Title;
            song.IsLiked = req.IsLiked;

            // 3. Update Artist (Logika Cerdik: Cek dulu apakah berubah?)
            // Jika user mengirim nama artis baru, kita harus cari/buat artisnya
            if (!string.IsNullOrEmpty(req.Artist) && song.Artist.Name != req.Artist)
            {
                var artist = _context.Artists.FirstOrDefault(a => a.Name == req.Artist);
                if (artist == null)
                {
                    artist = new Artist { Name = req.Artist };
                    _context.Artists.Add(artist);
                }
                song.Artist = artist;
            }

            // 4. Update Album
            // Album bergantung pada Artis. Jadi jika Artis berubah, Album juga harus dicek ulang.
            if (!string.IsNullOrEmpty(req.Album))
            {
                // Cek apakah album ini sudah ada untuk Artis yang (baru/lama) tersebut
                var currentArtistId = song.Artist.Id; // ID Artis yang sudah diset di langkah 3

                var album = _context.Albums
                    .FirstOrDefault(a => a.Title == req.Album && a.ArtistId == currentArtistId);

                if (album == null)
                {
                    // Cek local tracker (jika baru dibuat di memori langkah 3)
                    album = _context.Albums.Local
                        .FirstOrDefault(a => a.Title == req.Album && (a.ArtistId == currentArtistId || a.Artist == song.Artist));
                }

                if (album == null)
                {
                    album = new Album { Title = req.Album, Artist = song.Artist, Year = DateTime.Now.Year };
                    _context.Albums.Add(album);
                }
                song.Album = album;
            }

            // 5. Simpan Perubahan
            try
            {
                _context.SaveChanges();

                // Update file fisik metadata jika perlu (Opsional - butuh TagLib)
                // UpdateFileTags(song.FilePath, song.Title, song.Artist.Name, song.Album.Title); 

                return Ok(new
                {
                    message = "Data lagu berhasil diupdate!",
                    data = new
                    {
                        song.Id,
                        song.Title,
                        Artist = song.Artist.Name,
                        Album = song.Album.Title,
                        song.IsLiked
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Gagal menyimpan perubahan.", error = ex.Message });
            }
        }
    }

    // Class DTO (Data Transfer Object) untuk menampung data JSON dari Postman
    public class YoutubeImportDto
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public int DurationSec { get; set; }
    }

    // DTO untuk Register Path
    public class LocalPathDto
    {
        public string FilePath { get; set; }
    }

    // --- DTO BARU UNTUK UPDATE ---
    public class UpdateSongDto
    {
        public int Id { get; set; }          // Wajib: ID Lagu yang mau diedit
        public string Title { get; set; }    // Opsional: Judul baru
        public string Artist { get; set; }   // Opsional: Nama Artis baru
        public string Album { get; set; }    // Opsional: Nama Album baru
        public bool IsLiked { get; set; }    // Status Liked
    }
}