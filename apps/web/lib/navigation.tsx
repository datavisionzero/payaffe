import { useLocation, useNavigate, useSearchParams as useReactRouterSearchParams } from "react-router";

export function usePathname(): string {
  return useLocation().pathname;
}

export function useRouter() {
  const navigate = useNavigate();
  return {
    back: () => navigate(-1),
    forward: () => navigate(1),
    push: (href: string) => navigate(href),
    replace: (href: string) => navigate(href, { replace: true })
  };
}

export function useSearchParams(): URLSearchParams {
  return useReactRouterSearchParams()[0];
}
