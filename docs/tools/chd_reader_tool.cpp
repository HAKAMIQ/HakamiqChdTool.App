#include <libchdr/chd.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

using u8 = std::uint8_t;
using u32 = std::uint32_t;
using u64 = std::uint64_t;

class chd_reader
{
	std::string m_path;
	chd_file* m_chd = nullptr;
	u64 m_logical_size = 0;
	u32 m_hunk_size = 0;
	u32 m_total_hunks = 0;

	std::vector<u8> m_hunk_cache_data;
	std::vector<u32> m_hunk_cache_hunks;
	std::unordered_map<u32, std::size_t> m_hunk_cache_map;
	std::size_t m_next_hunk_cache_slot = 0;
	std::mutex m_mutex;

public:
	explicit chd_reader(std::string path, u64 decoded_cache_bytes = 8ull * 1024ull * 1024ull)
		: m_path(std::move(path))
	{
		chd_error err = chd_open(m_path.c_str(), CHD_OPEN_READ, nullptr, &m_chd);
		if (err != CHDERR_NONE)
		{
			throw std::runtime_error("chd_open failed: " + std::string(chd_error_string(err)));
		}

		const chd_header* header = chd_get_header(m_chd);
		if (!header || !header->logicalbytes || !header->hunkbytes)
		{
			throw std::runtime_error("invalid CHD header");
		}

		m_logical_size = header->logicalbytes;
		m_hunk_size = header->hunkbytes;
		m_total_hunks = header->totalhunks;

		const u64 entries = std::max<u64>(1, decoded_cache_bytes / m_hunk_size);
		const u64 capped_entries = std::min<u64>(entries, 4096);

		m_hunk_cache_data.resize(capped_entries * m_hunk_size);
		m_hunk_cache_hunks.assign(static_cast<std::size_t>(capped_entries), std::numeric_limits<u32>::max());
		m_hunk_cache_map.reserve(static_cast<std::size_t>(capped_entries));
	}

	chd_reader(const chd_reader&) = delete;
	chd_reader& operator=(const chd_reader&) = delete;

	~chd_reader()
	{
		if (m_chd)
		{
			chd_close(m_chd);
			m_chd = nullptr;
		}
	}

	u64 logical_size() const
	{
		return m_logical_size;
	}

	u32 hunk_size() const
	{
		return m_hunk_size;
	}

	u32 total_hunks() const
	{
		return m_total_hunks;
	}

	std::size_t decoded_cache_bytes() const
	{
		return m_hunk_cache_data.size();
	}

	u64 read_at(u64 offset, void* output, u64 size)
	{
		std::lock_guard lock(m_mutex);

		if (offset >= m_logical_size || size == 0)
		{
			return 0;
		}

		u64 remaining = std::min<u64>(size, m_logical_size - offset);
		u64 total_read = 0;
		u8* out = static_cast<u8*>(output);

		while (remaining)
		{
			const u32 hunk = static_cast<u32>(offset / m_hunk_size);
			const u64 hunk_offset = offset % m_hunk_size;
			const u64 copy_size = std::min<u64>(remaining, m_hunk_size - hunk_offset);
			const u8* hunk_data = read_hunk(hunk);

			std::memcpy(out + total_read, hunk_data + hunk_offset, static_cast<std::size_t>(copy_size));

			offset += copy_size;
			total_read += copy_size;
			remaining -= copy_size;
		}

		return total_read;
	}

private:
	const u8* read_hunk(u32 hunk)
	{
		const auto cached = m_hunk_cache_map.find(hunk);
		if (cached != m_hunk_cache_map.cend())
		{
			return m_hunk_cache_data.data() + cached->second * m_hunk_size;
		}

		const std::size_t slot = m_next_hunk_cache_slot;
		m_next_hunk_cache_slot = (m_next_hunk_cache_slot + 1) % m_hunk_cache_hunks.size();

		if (m_hunk_cache_hunks[slot] != std::numeric_limits<u32>::max())
		{
			m_hunk_cache_map.erase(m_hunk_cache_hunks[slot]);
		}

		u8* dest = m_hunk_cache_data.data() + slot * m_hunk_size;
		const chd_error err = chd_read(m_chd, hunk, dest);

		if (err != CHDERR_NONE)
		{
			m_hunk_cache_hunks[slot] = std::numeric_limits<u32>::max();
			throw std::runtime_error("chd_read failed at hunk " + std::to_string(hunk) + ": " + chd_error_string(err));
		}

		m_hunk_cache_hunks[slot] = hunk;
		m_hunk_cache_map[hunk] = slot;
		return dest;
	}
};

static u64 parse_u64(const std::string& text)
{
	std::size_t pos = 0;
	const int base = text.rfind("0x", 0) == 0 || text.rfind("0X", 0) == 0 ? 16 : 10;
	const u64 value = std::stoull(text, &pos, base);
	if (pos != text.size())
	{
		throw std::runtime_error("invalid number: " + text);
	}
	return value;
}

static void print_usage()
{
	std::cerr
		<< "Usage:\n"
		<< "  chd_reader_tool.exe info <file.chd>\n"
		<< "  chd_reader_tool.exe dump <file.chd> <offset> <size> <out.bin>\n"
		<< "  chd_reader_tool.exe scan <file.chd> [bytes_to_read]\n";
}

int main(int argc, char** argv)
{
	try
	{
		if (argc < 3)
		{
			print_usage();
			return 2;
		}

		const std::string command = argv[1];
		const std::string path = argv[2];
		chd_reader reader(path);
		const auto physical_size = std::filesystem::file_size(path);

		if (command == "info")
		{
			std::cout << "path=" << path << "\n";
			std::cout << "physical_bytes=" << physical_size << "\n";
			std::cout << "logical_bytes=" << reader.logical_size() << "\n";
			std::cout << "hunk_bytes=" << reader.hunk_size() << "\n";
			std::cout << "total_hunks=" << reader.total_hunks() << "\n";
			std::cout << "decoded_cache_bytes=" << reader.decoded_cache_bytes() << "\n";
			return 0;
		}

		if (command == "dump")
		{
			if (argc != 6)
			{
				print_usage();
				return 2;
			}

			const u64 offset = parse_u64(argv[3]);
			const u64 size = parse_u64(argv[4]);
			const std::string out_path = argv[5];

			std::ofstream out(out_path, std::ios::binary);
			if (!out)
			{
				throw std::runtime_error("failed to open output file");
			}

			std::vector<u8> buffer(1024 * 1024);
			u64 done = 0;

			while (done < size)
			{
				const u64 chunk = std::min<u64>(buffer.size(), size - done);
				const u64 read = reader.read_at(offset + done, buffer.data(), chunk);
				if (!read)
				{
					break;
				}
				out.write(reinterpret_cast<const char*>(buffer.data()), static_cast<std::streamsize>(read));
				done += read;
			}

			std::cout << "dumped_bytes=" << done << "\n";
			return 0;
		}

		if (command == "scan")
		{
			const u64 requested = argc >= 4 ? parse_u64(argv[3]) : reader.logical_size();
			const u64 size = std::min<u64>(requested, reader.logical_size());
			std::vector<u8> buffer(4 * 1024 * 1024);

			const auto start = std::chrono::steady_clock::now();
			u64 done = 0;

			while (done < size)
			{
				const u64 chunk = std::min<u64>(buffer.size(), size - done);
				const u64 read = reader.read_at(done, buffer.data(), chunk);
				if (!read)
				{
					break;
				}
				done += read;
			}

			const auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
			const double mib = static_cast<double>(done) / 1024.0 / 1024.0;
			std::cout << "read_bytes=" << done << "\n";
			std::cout << "seconds=" << std::fixed << std::setprecision(3) << elapsed << "\n";
			std::cout << "MiB_per_second=" << std::fixed << std::setprecision(2) << (elapsed > 0.0 ? mib / elapsed : 0.0) << "\n";
			return 0;
		}

		print_usage();
		return 2;
	}
	catch (const std::exception& e)
	{
		std::cerr << "error: " << e.what() << "\n";
		return 1;
	}
}
