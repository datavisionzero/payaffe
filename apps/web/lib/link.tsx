import { Link as ReactRouterLink, type LinkProps as ReactRouterLinkProps } from "react-router";

type LinkProps = Omit<ReactRouterLinkProps, "to"> & { href: ReactRouterLinkProps["to"] };

export default function Link({ href, ...props }: LinkProps) {
  return <ReactRouterLink to={href} {...props} />;
}
