import createNextIntlPlugin from "next-intl/plugin";
import type { NextConfig } from "next";

const withNextIntl = createNextIntlPlugin("./messages/request.ts");

const nextConfig: NextConfig = {
  output: "standalone",
  typedRoutes: true
};

export default withNextIntl(nextConfig);
