import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "ArchInsight AI | Architecture intelligence",
  description: "A clear view of your software landscape and its next steps.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
