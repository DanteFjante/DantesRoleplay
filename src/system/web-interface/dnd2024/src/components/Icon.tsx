import { createElement } from "react";
import {
  ArrowRight,
  BookOpen,
  Castle,
  ChevronRight,
  CircleUserRound,
  Clock3,
  Compass,
  Eye,
  Globe2,
  Landmark,
  LocateFixed,
  Map,
  MapPin,
  Maximize2,
  Mountain,
  PackageOpen,
  Route,
  RotateCcw,
  ScrollText,
  Search,
  Shield,
  Sparkles,
  Swords,
  TreePine,
  UsersRound,
  ZoomIn,
  ZoomOut,
  Focus,
} from "lucide";

type IconNodeChild = readonly [tag: string, attrs: Record<string, string | number>];
type IconNode = readonly [
  tag: string,
  attrs: Record<string, string | number>,
  children?: readonly IconNodeChild[],
];

const iconNodes: Record<string, IconNode> = {
  ArrowRight,
  BookOpen,
  Castle,
  ChevronRight,
  CircleUserRound,
  Clock3,
  Compass,
  Eye,
  Globe2,
  Landmark,
  LocateFixed,
  Map,
  MapPin,
  Maximize2,
  Mountain,
  PackageOpen,
  Route,
  RotateCcw,
  ScrollText,
  Search,
  Shield,
  Sparkles,
  Swords,
  TreePine,
  UsersRound,
  ZoomIn,
  ZoomOut,
  Focus,
};

function reactAttributes(attributes: Record<string, string | number>) {
  return Object.fromEntries(
    Object.entries(attributes).map(([key, value]) => [
      key.replace(/-([a-z])/g, (_match, letter: string) => letter.toUpperCase()),
      value,
    ]),
  );
}

export function Icon({ name, size = 18, className }: { name: string; size?: number; className?: string }) {
  const node = iconNodes[name] ?? Sparkles;
  const [, , children = []] = node;

  return (
    <svg
      aria-hidden="true"
      className={className}
      fill="none"
      height={size}
      stroke="currentColor"
      strokeLinecap="round"
      strokeLinejoin="round"
      strokeWidth="2"
      viewBox="0 0 24 24"
      width={size}
    >
      {children.map(([tag, attributes], index) =>
        createElement(tag, { ...reactAttributes(attributes), key: `${tag}-${index}` }),
      )}
    </svg>
  );
}
