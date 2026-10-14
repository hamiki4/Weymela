import { describe, expect, it } from "vitest";
import { normalizeTikTokVideoLink } from "../src/features/creator/tiktokVideoLink";

describe("TikTok submission link validation", () => {
  it.each([
    ["https://www.tiktok.com/@creator/video/7412345678901234567/", "https://www.tiktok.com/@creator/video/7412345678901234567"],
    ["https://www.tiktok.com/t/ZMabcdef/", "https://www.tiktok.com/t/ZMabcdef"],
    ["https://vm.tiktok.com/ZMabcdef/", "https://vm.tiktok.com/ZMabcdef"],
    ["https://m.tiktok.com/@creator/video/7412345678901234567?share=1", "https://www.tiktok.com/@creator/video/7412345678901234567"],
    ["https://www.tiktok.com/t/ZMabcdef?redirect=https://evil.example", "https://www.tiktok.com/t/ZMabcdef"],
  ])("normalizes supported links", (input, expected) => {
    expect(normalizeTikTokVideoLink(input)).toBe(expected);
  });

  it.each([
    "http://www.tiktok.com/t/ZMabcdef",
    "https://tiktok.com.evil.example/t/ZMabcdef",
    "https://example.com/@creator/video/7412345678901234567",
    "https://www.tiktok.com/t/short",
    "https://user@www.tiktok.com/t/ZMabcdef",
  ])("rejects unsafe or malformed URL %s", (input) => {
    expect(normalizeTikTokVideoLink(input)).toBeNull();
  });
});
