import { Icon } from "./Icon";

export function CampaignEmptyState({
  icon,
  title,
  description,
}: {
  icon: string;
  title: string;
  description: string;
}) {
  return (
    <div className="campaign-empty">
      <Icon name={icon} />
      <strong>{title}</strong>
      <p>{description}</p>
    </div>
  );
}
